# FSX

## Motivation

FSX is the ideal tool for people that use F# for their scripting needs.

The best way to describe it is to start first with some questions:
* Have you found yourself waiting many seconds until your big script is parsed by FSI and run? This is unacceptable when doing many small changes and expecting a quick feedback loop to test them.
* Do you have long-running F# scripts that cause too much memory usage in your server?
* Have you found that your scripts could bitrot over time (i.e. not compile anymore) especially when using helper functions in .fs files loaded by them?

These are the main annoyances when working with F# scripting. Granted, F#+FSI is already much better than the alternatives (as many more errors are thrown much earlier than at runtime, and as strongly-typed functional languages are generally faster). However, we can do better.

To the above three questions we could even follow-up with new ones:
* Couldn't we make FSI only compile what's changed, and reuse binaries from a previous run, to speed this up?
* Couldn't we run our script without FSI given that FSI eats a lot of memory (for REPL features, which scripts don't need)?
* Couldn't we have a CI approach that takes care of our scripts in a similar way as we do with (msbuild-ed) C#/F# code?

FSX answers all of these latter questions with a categorical YES!

The creation of FSX was inspired by several facts:
* FSI is slower than the F# compiler (obviously).
* There should be an easy and programatic way to compile an F# script without trying to run it (see https://stackoverflow.com/questions/33468298/f-how-can-i-compile-and-then-release-a-file-fsx ).
* FSI stands for F Sharp **Interactive**, which means that it's not really suited for scripting but more for debugging:
  * It doesn't treat warnings as errors by default (you would need to remember to use the flag --warnaserror when calling fsharpi, which is not handy).
  * Because of the previous point above about warnings, it can even cancel the advantage of the promise of "statically-compiled scripts" altogether, because what should be a compilation error could be translated to a runtime error when using currified arguments, due to FSI defaulting to "interactive" needs. (More info: https://stackoverflow.com/questions/38202685/fsx-script-ignoring-a-function-call-when-i-add-a-parameter-to-it )
  * AFAIK dotnet fsi doesn't have a `--warnaserror` flag. Note that fsx will treat warnings as errors by default.
  * It can consume a lot of memory, just compare it this way:

```
echo $'#!/usr/bin/env -S dotnet fsi\nSystem.Threading.Thread.Sleep(999999999)'>testfsi.fsx
echo $'#!/usr/bin/env fsx\nSystem.Threading.Thread.Sleep(999999999)'>testfsx.fsx
chmod u+x test*.fsx
nohup ./testfsi.fsx >/dev/null 2>&1 &
nohup ./testfsx.fsx >/dev/null 2>&1 &
ps aux | grep testfs
```

In my machine, the above prints:
```
knocte@Ubuntu22:~/Documents/fsx$ ps aux | grep testfs
knocte     22388  0.0  2.2 2921872 44288 pts/1   Sl   12:33   0:00 dotnet fsi ./testfsi.fsx
knocte     22398  0.6  7.5 2934240 150948 pts/1  Sl   12:33   0:01 /usr/lib/dotnet/dotnet exec /usr/lib/dotnet/sdk/6.0.121/FSharp/fsi.dll ./testfsi.fsx
knocte     22409  0.0  1.0 2741472 20352 pts/1   Sl   12:33   0:00 dotnet ./bin/testfsx.fsx.dll
```

Which is a huge difference in memory footprint.


## How to install/use?


### Installation

In Linux/macOS, the old-fashioned way by cloning and compiling it yourself:

```
./configure.sh --prefix=/usr/local
make
sudo make install
```

(If you're using Windows, just build with "make.bat" and install with "make install".)


### Usage


#### Execution

After installing, you can already use the `#!/usr/bin/env fsx` shebang in your scripts.

If you want to use fsx without having to change the shebang of all your scripts, just
run `fsx yourscript.fsx` every time.


#### Compilation

For your CI needs (to compile all scripts in your repo without executing them), you could call `fsxc` using `find` in your CI step.

An example of how to do this with GitHub Actions, is this YML fragment that you could add to your workflow existing in your `.github/workflows/` folder:

```
    - name: compile F# scripts
      shell: bash
      run: |
        dotnet new tool-manifest
        dotnet tool install fsxc
        find . -type f -name "*.fsx" | xargs -t -I {} dotnet fsxc {}
```


### Roadmap

* Remove legacy framework support (so that build system can converge into .fsx files instead of autotools in Unix + fsx in Windows).
* Allow fsxc & fsx disable warnAsError (via -w flag? or --ignore-warnings).
* Try creating VMs for CI that uninstall .NETCore/.NET6 completely (not just the dotnet executable removal hack), to make sure legacy framework build still works there.
* Try creating VMs for CI that uninstall Mono/.NET4.x completey (e.g. for macOS see: https://github.com/mono/website/commit/490797429d4b92584394292ff69fbdc0eb002948 )

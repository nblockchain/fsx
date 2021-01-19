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
* FSI (or the components required to run it) suffers from bugs frequently. Examples:
  * If your version of Mono is too old (e.g. 4.6.2, the version that comes by default in Ubuntu 18.04), then it might crash with a segmentation fault. More info: https://bugzilla.xamarin.com/show_bug.cgi?id=42417 .
  * If your version of Mono is not too old, but your version of F# is not too new (e.g. what happens exactly with Ubuntu 19.04), then FSI might not work at all. More info: https://github.com/fsharp/fsharp/issues/740 .
* FSI stands for F Sharp **Interactive**, which means that it's not really suited for scripting but more for debugging:
  * It doesn't treat warnings as errors by default (you would need to remember to use the flag --warnaserror when calling fsharpi, which is not handy).
  * Because of the previous point above about warnings, it can even cancel the advantage of the promise of "statically-compiled scripts" altogether, because what should be a compilation error could be translated to a runtime error when using currified arguments, due to FSI defaulting to "interactive" needs. (More info: https://stackoverflow.com/questions/38202685/fsx-script-ignoring-a-function-call-when-i-add-a-parameter-to-it )
  * AFAIK there's no way to use flags in a shebang (so can't use `#!/usr/bin/env fsharpi --warnaserror` as the flag gets ignored). Note that using fsx in shebang, however, will treat warnings as errors.
  * It can consume a lot of memory, just compare it this way:

```
echo $'#!/usr/bin/env fsharpi\nSystem.Threading.Thread.Sleep(999999999)'>testfsi.fsx
echo $'#!/usr/bin/env fsx\nSystem.Threading.Thread.Sleep(999999999)'>testfsx.fsx
chmod u+x test*.fsx
nohup ./testfsi.fsx >/dev/null 2>&1 &
nohup ./testfsx.fsx >/dev/null 2>&1 &
ps aux | grep testfs
```

In my machine, the above prints:
```
andres   23596 16.6  0.9 254504 148268 pts/24  Sl   03:38   0:01 cli /usr/lib/cli/fsharp/fsi.exe --exename:fsharpi ./testfsi.fsx
andres   23600  0.0  0.0 129332 15936 pts/24   Sl   03:38   0:00 mono bin/./testfsx.fsx.exe
```

Which is a huge difference in memory footprint.


## How to install/use?


### Installation

There are two ways to install fsx; the old-fashioned way by cloning and compiling it yourself:

```
./configure.sh --prefix=/usr/local
make
sudo make install
```

Or simply by installing the snap package. You can grab it from the artifacts of our CI build by clicking on the icon on the right-top corner in the following page:
https://gitlab.com/knocte/fsx/pipelines?ref=master

After downloading and decompressing the artifacts, you have to use the command line:

```
snap install --dangerous --classic fsx*.snap
```

### Usage

After installing, you can already use the `#!/usr/bin/env fsx` shebang in your scripts.

If you want to use fsx without having to change the shebang of all your scripts, just
run `fsx yourscript.fsx` every time.

For your CI needs, you could include fsx repository as a submodule, and then bootstrap it in your CI script, and call `ci-build.fsx`, which will find all the F# script files in your repository and try to compile them (but not run them). An example of how to do this with GitLabCI, is this `.gitlab-ci.yml` configuration file sample:

```
image: ubuntu:18.04
before_script:
  - apt-get update -qq
  - apt-get install -y -qq git
  - git submodule sync --recursive
  - git submodule update --init --recursive
  - apt-get install -y -qq fsharp
build:
  script:
    - ./fsx/ci-build.fsx
```


# FSX

FSX is the ideal tool for DevOps people that use F# in their sysadmin scripts.

The best way to describe it is to start first with some questions:
* Have you found yourself waiting many seconds until your big script is parsed by FSI and run? This is unacceptable when doing many small changes and expecting a quick feedback loop to test them.
* Do you have long-running F# scripts that cause too much memory usage in your server?
* Have you found that your scripts could bitrot over time (i.e. not compile anymore) especially when using helper functions in .fs files loaded by them?

These are the two main annoyances when working with F# scripting. Granted, F#+FSI is already much better than the alternatives (as many more errors are thrown much earlier than at runtime, and as strongly-typed functional languages are generally faster). However, we can do better.

To the above two questions we could even follow-up with new ones:
* Couldn't we make FSI only compile what's changed, and reuse binaries from a previous run, to speed this up?
* Couldn't we run our script without FSI given that FSI eats a lot of memory (for REPL features, which scripts don't need)?
* Couldn't we have a CI approach that takes care of our scripts in a similar way as we do with C# code?

FSX answers both of these latter questions with a categorical YES!

# How to use?
In debian-based Linux, instead of running `fsharpi` command, you could use fsx instead, which would reuse binaries from a previous run, or generate new binaries first if needed.
You could even install it system wide, and use it in your hashbang too (instead of `#!/usr/bin/env fsharpi`, use `#!/usr/bin/env fsx`).

For your CI needs, you could include fsx repository as a submodule, and then bootstrap it in your CI script, and call `build.fsx`, which will find all the F# script files in your repository and try to compile them (but not run them). An example of how to do this with GitLabCI, is this `.gitlab-ci.yml` configuration file sample:

```
image: ubuntu:16.04
before_script:
  - apt-get update -qq
  - apt-get install -y -qq git
  - git submodule sync --recursive
  - git submodule update --init --recursive
  - apt-get install -y -qq fsharp
build:
  script:
    - ./fsx/build.fsx
```

# Acknowledgements
This work is half done in my spare time, and half sponsored by Gatecoin due to our infrastructure needs. It is released under MIT/X11 license.

The creation of FSX was inspired by several facts:
* FSI is slower than the F# compiler, and it has some bugs (e.g.: https://bugzilla.xamarin.com/show_bug.cgi?id=42417).
* FSI stands for F Sharp **Interactive**, which means that it's not really suited for scripting but more for debugging:
  * It can even break altogether the advantage of the promise of "statically-compiled scripts", because what should be a compilation error could be translated to a runtime error when using currified arguments, due to FSI defaulting to "interactive" needs. (More info: https://stackoverflow.com/questions/38202685/fsx-script-ignoring-a-function-call-when-i-add-a-parameter-to-it )
  * It can consume a lot of memory, just compare it this way:
```
echo $'#!/usr/bin/env fsharpi\nSystem.Threading.Thread.Sleep(System.TimeSpan.FromMinutes(999.0))'>testfsx.fsx
chmod u+x testfsx.fsx
nohup ./testfsx.fsx &
./fsx.fsx -c testfsx.fsx
nohup mono bin/testfsx.exe &
ps aux | grep testfsx
```
In my machine, the above prints:
```
andres   11371  3.4  0.8 254588 144212 pts/17  Sl   13:37   0:02 cli /usr/lib/cli/fsharp/fsi.exe --exename:fsharpi ./testfsx.fsx
andres   11410  0.2  0.0 129332 13700 pts/17   Sl   13:37   0:00 mono bin/testfsx.exe
```
Which is a huge difference in memory footprint.

* There should be an easy and programatic way to compile an F# script without trying to run it (see https://stackoverflow.com/questions/33468298/f-how-can-i-compile-and-then-release-a-file-fsx ).

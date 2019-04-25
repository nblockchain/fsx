#!/usr/bin/env fsharpi

open System
open System.IO

#r "System.Configuration"
#load "InfraLib/Misc.fs"
#load "InfraLib/Process.fs"
open FSX.Infrastructure
open Process

type BinaryConfig =
    | Debug
    | Release
    override self.ToString() =
        sprintf "%A" self

let rec private GatherTarget(args: string list, targetSet: Option<string>): Option<string> =
    match args with
    | [] -> targetSet
    | head::tail ->
        if (targetSet.IsSome) then
            failwith "only one target can be passed to make"
        GatherTarget(tail, Some(head))

let GatherPrefix(): string =
    let buildConfig = FileInfo(Path.Combine(__SOURCE_DIRECTORY__, "build.config"))
    if not (buildConfig.Exists) then
        Console.Error.WriteLine ("ERROR: configure hasn't been run yet, run ./configure.sh first")
        Environment.Exit 1
    let buildConfigContents = File.ReadAllText(buildConfig.FullName)
    buildConfigContents.Substring("Prefix=".Length).Trim()

let prefix = GatherPrefix()
let fsxInstallDir = Path.Combine(prefix, "lib", "fsx")
                    |> DirectoryInfo
let binInstallDir = Path.Combine(prefix, "bin")
                    |> DirectoryInfo

let localBinFolder = Path.Combine(__SOURCE_DIRECTORY__, "bin")
let fsxLauncherScriptPath = FileInfo(Path.Combine(localBinFolder, "fsx"))

let wrapperFsxScript = """#!/bin/sh
set -e

which fsharpc >/dev/null || \
  (echo "Please install fsharp package first via apt" && exit 2)

if [ $# -lt 1 ]; then
    echo "At least one argument expected"
    exit 1
fi

DIR_OF_THIS_SCRIPT=$(dirname "$(realpath "$0")")
FSXC_PATH="$DIR_OF_THIS_SCRIPT/../lib/fsx/fsxc.exe"
mono "$FSXC_PATH" "$1"
TARGET_DIR=$(dirname -- "$1")
TARGET_FILE=$(basename -- "$1")
shift
exec mono "$TARGET_DIR/bin/$TARGET_FILE.exe" "$@"
"""

let JustBuild binaryConfig =
    Console.WriteLine("Compiling fsx...")
    let xbuildArgs = sprintf "fsx.sln /p:Configuration=%s" (binaryConfig.ToString())
    let xbuildProc = Process.Execute({ Command = "xbuild"; Arguments = xbuildArgs }, Echo.All)
    if xbuildProc.ExitCode <> 0 then
        Environment.Exit 1

    Directory.CreateDirectory localBinFolder |> ignore
    File.WriteAllText(fsxLauncherScriptPath.FullName,
                      wrapperFsxScript)


let maybeTarget = GatherTarget(Misc.FsxArguments(), None)
match maybeTarget with
| None -> JustBuild BinaryConfig.Debug
| Some(target) ->
    if (target = "install") then
        let releaseConfig = BinaryConfig.Release
        JustBuild releaseConfig
        let fsxcBinary = FileInfo(Path.Combine("fsxc", "bin", releaseConfig.ToString(), "fsxc.exe"))

        Console.WriteLine("Installing fsx...")
        Console.WriteLine()
        fsxInstallDir.Create()
        File.Copy(fsxcBinary.FullName, Path.Combine(fsxInstallDir.FullName, fsxcBinary.Name), true)

        binInstallDir.Create()
        let finalPrefixPathOfWrapperScript = Path.Combine(binInstallDir.FullName, fsxLauncherScriptPath.Name)
        File.Copy(fsxLauncherScriptPath.FullName, finalPrefixPathOfWrapperScript, true)
        if ((Process.Execute({ Command = "chmod"; Arguments = sprintf "ugo+x %s" finalPrefixPathOfWrapperScript }, Echo.Off)).ExitCode <> 0) then
            failwith "Unexpected chmod failure, please report this bug"
    else
        Console.Error.WriteLine("Unrecognized target: " + target)
        Environment.Exit 2

#!/usr/bin/env fsharpi

open System
open System.IO

#r "System.Configuration"
#load "InfraLib/MiscTools.fs"
#load "InfraLib/ProcessTools.fs"
open FSX.Infrastructure
open ProcessTools

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
let fsxInstallPath = Path.Combine(prefix, "lib", "fsx")
let binInstallPath = Path.Combine(prefix, "bin")

let fsxLauncherScriptPath = FileInfo(Path.Combine(__SOURCE_DIRECTORY__, "bin", "fsx"))
let fsxBinaryPath = FileInfo(Path.Combine(__SOURCE_DIRECTORY__, "bin", "fsxc.fsx.exe"))

let wrapperFsxScript = """#!/bin/sh
set -e

if [ $# -lt 1 ]; then
    echo "At least one argument expected"
    exit 1
fi

mono {0}/fsxc.fsx.exe "$1"
TARGET_DIR=$(dirname -- "$1")
TARGET_FILE=$(basename -- "$1")
shift
exec mono "$TARGET_DIR/bin/$TARGET_FILE.exe" "$@"
"""

let JustBuild() =
    Console.WriteLine("Compiling fsx...")
    let fsxPath = Path.Combine(__SOURCE_DIRECTORY__, "fsxc.fsx")
    let fsharpcWhich = ProcessTools.Execute({ Command = fsxPath; Arguments = fsxPath }, Echo.All)
    if (fsharpcWhich.ExitCode <> 0) then
        Environment.Exit 1

    File.WriteAllText(fsxLauncherScriptPath.FullName,
                      String.Format(wrapperFsxScript, fsxInstallPath))


let maybeTarget = GatherTarget(MiscTools.FsxArguments(), None)
match maybeTarget with
| None -> JustBuild()
| Some(target) ->
    if (target = "install") then
        Console.WriteLine("Installing fsx...")
        Console.WriteLine()
        Directory.CreateDirectory(fsxInstallPath) |> ignore
        File.Copy(fsxBinaryPath.FullName, Path.Combine(fsxInstallPath, fsxBinaryPath.Name), true)

        let finalPrefixPathOfWrapperScript = Path.Combine(binInstallPath, fsxLauncherScriptPath.Name)
        File.Copy(fsxLauncherScriptPath.FullName, finalPrefixPathOfWrapperScript, true)
        if ((ProcessTools.Execute({ Command = "chmod"; Arguments = sprintf "ugo+x %s" finalPrefixPathOfWrapperScript }, Echo.Off)).ExitCode <> 0) then
            failwith "Unexpected chmod failure, please report this bug"
    else
        Console.Error.WriteLine("Unrecognized target: " + target)
        Environment.Exit 2

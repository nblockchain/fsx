#!/usr/bin/env fsharpi

open System
open System.IO
#load "Infra.fs"
open FSX.Infrastructure

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
let fsxBinaryPath = FileInfo(Path.Combine(__SOURCE_DIRECTORY__, "bin", "fsx.fsx.exe"))

let wrapperFsxScript = """
#!/bin/sh
set -e
mono {0}/fsx.fsx.exe -c "$@"
TARGET_DIR=$(dirname -- "$1")
TARGET_FILE=$(basename -- "$1")
exec mono "$TARGET_DIR/bin/$TARGET_FILE.exe"
"""

let JustBuild() =
    Console.WriteLine("Compiling fsx...")
    let fsxPath = Path.Combine(__SOURCE_DIRECTORY__, "fsx.fsx")
    let fsharpcWhich = Process.Execute(sprintf "%s -c %s" fsxPath fsxPath, true, false)
    if (fsharpcWhich.ExitCode <> 0) then
        Environment.Exit 1

    File.WriteAllText(fsxLauncherScriptPath.FullName,
                      String.Format(wrapperFsxScript, fsxInstallPath))


let maybeTarget = GatherTarget(Util.FsxArguments(), None)
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
        if ((Process.Execute(sprintf "chmod ugo+x %s" finalPrefixPathOfWrapperScript, false, true)).ExitCode <> 0) then
            failwith "Unexpected chmod failure, please report this bug"
    else
        Console.Error.WriteLine("Unrecognized target: " + target)
        Environment.Exit 2

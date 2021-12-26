#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq
open System.Diagnostics

open System.Text
open System.Text.RegularExpressions
#r "System.Core.dll"
open System.Xml
#r "System.Xml.Linq.dll"
open System.Xml.Linq
open System.Xml.XPath

#r "System.Configuration"
open System.Configuration
#load "../InfraLib/Misc.fs"
#load "../InfraLib/Process.fs"
#load "../InfraLib/Git.fs"
open FSX.Infrastructure
open Process

type BinaryConfig =
    | Debug
    | Release
    override self.ToString() =
        sprintf "%A" self

match Misc.GuessPlatform() with
| Misc.Platform.Windows -> ()
| _ ->
    failwith "This make script was only meant to be run on Windows, not Linux/macOS"

let scriptsDir = DirectoryInfo __SOURCE_DIRECTORY__
let rootDir = Path.Combine(scriptsDir.FullName, "..") |> DirectoryInfo
let UNIX_NAME = "fsx"

let rec private GatherTarget (args: string list, targetSet: Option<string>): Option<string> =
    match args with
    | [] -> targetSet
    | head::tail ->
        if targetSet.IsSome then
            failwith "only one target can be passed to make"
        GatherTarget (tail, Some head)

let buildConfigFileName = "build.config"
let buildConfigContents =
    let buildConfig =
        Path.Combine (scriptsDir.FullName, buildConfigFileName)
        |> FileInfo
    if not (buildConfig.Exists) then
        let configureLaunch = ".\\configure.bat"
        Console.Error.WriteLine (sprintf "ERROR: configure hasn't been run yet, run %s first"
                                         configureLaunch)
        Environment.Exit 1

    let skipBlankLines line = not <| String.IsNullOrWhiteSpace line
    let splitLineIntoKeyValueTuple (line:string) =
        let pair = line.Split([|'='|], StringSplitOptions.RemoveEmptyEntries)
        if pair.Length <> 2 then
            failwithf "All lines in %s must conform to format:\n\tkey=value"
                      buildConfigFileName
        pair.[0], pair.[1]

    let buildConfigContents =
        File.ReadAllLines buildConfig.FullName
        |> Array.filter skipBlankLines
        |> Array.map splitLineIntoKeyValueTuple
        |> Map.ofArray
    buildConfigContents

let GetOrExplain key map =
    match map |> Map.tryFind key with
    | Some k -> k
    | None   -> failwithf "No entry exists in %s with a key '%s'."
                          buildConfigFileName key

let prefix = buildConfigContents |> GetOrExplain "Prefix"
let libPrefixDir = DirectoryInfo (Path.Combine (prefix, "lib", UNIX_NAME))
let binPrefixDir = DirectoryInfo (Path.Combine (prefix, "bin"))

let BuildSolution
    (buildTool: string)
    (solutionFileName: string)
    (binaryConfig: BinaryConfig)
    (maybeConstant: Option<string>)
    (extraOptions: string)
    =
    let configOption = sprintf "/p:Configuration=%s" (binaryConfig.ToString())
    let defineConstantsFromBuildConfig =
        match buildConfigContents |> Map.tryFind "DefineConstants" with
        | Some constants -> constants.Split([|";"|], StringSplitOptions.RemoveEmptyEntries) |> Seq.ofArray
        | None -> Seq.empty
    let allDefineConstants =
        match maybeConstant with
        | Some constant -> Seq.append [constant] defineConstantsFromBuildConfig
        | None -> defineConstantsFromBuildConfig
    let configOptions =
        if allDefineConstants.Any() then
            // FIXME: we shouldn't override the project's DefineConstants, but rather set "ExtraDefineConstants"
            // from the command line, and merge them later in the project file: see https://stackoverflow.com/a/32326853/544947
            sprintf "%s;DefineConstants=%s" configOption (String.Join(";", allDefineConstants))
        else
            configOption
    let buildArgs = sprintf "%s %s %s"
                            solutionFileName
                            configOptions
                            extraOptions
    let buildProcess = Process.Execute ({ Command = buildTool; Arguments = buildArgs }, Echo.All)
    if buildProcess.ExitCode <> 0 then
        Console.Error.WriteLine (sprintf "%s build failed" buildTool)
        Environment.Exit 1

let JustBuild binaryConfig maybeConstant =
    let buildTool = Map.tryFind "BuildTool" buildConfigContents
    if buildTool.IsNone then
        failwith "A BuildTool should have been chosen by the configure script, please report this bug"

    Console.WriteLine (sprintf "Building in %s mode..." (binaryConfig.ToString()))
    BuildSolution
        buildTool.Value
        (sprintf "%s.sln" UNIX_NAME)
        binaryConfig
        maybeConstant
        String.Empty

(* TODO: add launcher-script creation
    let launcherScriptFile =
        Path.Combine (FsxHelper.ScriptsDir.FullName, "bin", "fsx.bat")
        |> FileInfo
    let launcherScript = """
...
"""

    Directory.CreateDirectory(launcherScriptFile.Directory.FullName) |> ignore
    let launcherScriptWithPaths =
        launcherScript.Replace("$UNIX_NAME", UNIX_NAME)
                     .Replace("$GWALLET_PROJECT", DEFAULT_FRONTEND)
    File.WriteAllText (launcherScriptFile.FullName, launcherScriptWithPaths)
*)

let MakeCheckCommand (commandName: string) =
    if not (Process.CommandWorksInShell commandName) then
        Console.Error.WriteLine (sprintf "%s not found, please install it first" commandName)
        Environment.Exit 1

let MakeAll (maybeConstant: Option<string>) =
    let buildConfig = BinaryConfig.Debug
    JustBuild buildConfig maybeConstant
    buildConfig

let maybeTarget = GatherTarget (Misc.FsxOnlyArguments(), None)
match maybeTarget with
| None ->
    MakeAll None |> ignore

| Some "release" ->
    JustBuild BinaryConfig.Release None

(* TODO: add a way to run the tests from here
| Some "check" ->
    Console.WriteLine "Running tests..."
    Console.WriteLine ()

    // so that we get file names in stack traces
    Environment.SetEnvironmentVariable("MONO_ENV_OPTIONS", "--debug")

    let testsRun = ...
    if (testsRun.ExitCode <> 0) then
        Console.Error.WriteLine "Tests failed"
        Environment.Exit 1
*)

(* TODO: add an installation phase:
| Some "install" ->

    let mainBinariesDir binaryConfig =
        Path.Combine (
            rootDir.FullName,
            "src",
            DEFAULT_FRONTEND,
            "bin",
            binaryConfig.ToString())
        |> DirectoryInfo

    let buildConfig = BinaryConfig.Release
    JustBuild buildConfig None

    let destDirUpperCase = Environment.GetEnvironmentVariable "DESTDIR"
    let destDirLowerCase = Environment.GetEnvironmentVariable "DestDir"
    let destDir =
        if not (String.IsNullOrEmpty destDirUpperCase) then
            destDirUpperCase |> DirectoryInfo
        elif not (String.IsNullOrEmpty destDirLowerCase) then
            destDirLowerCase |> DirectoryInfo
        else
            prefix |> DirectoryInfo

    let libDestDir = Path.Combine(destDir.FullName, "lib", UNIX_NAME) |> DirectoryInfo
    let binDestDir = Path.Combine(destDir.FullName, "bin") |> DirectoryInfo

    Console.WriteLine "Installing..."
    Console.WriteLine ()
    Misc.CopyDirectoryRecursively (mainBinariesDir buildConfig, libDestDir, [])

    let finalLauncherScriptInDestDir = Path.Combine(binDestDir.FullName, launcherScriptFile.Name) |> FileInfo
    if not (Directory.Exists(finalLauncherScriptInDestDir.Directory.FullName)) then
        Directory.CreateDirectory(finalLauncherScriptInDestDir.Directory.FullName) |> ignore
    File.Copy(launcherScriptFile.FullName, finalLauncherScriptInDestDir.FullName, true)
    if Process.Execute({ Command = "chmod"; Arguments = sprintf "ugo+x %s" finalLauncherScriptInDestDir.FullName },
                        Echo.Off).ExitCode <> 0 then
        failwith "Unexpected chmod failure, please report this bug"
*)

| Some someOtherTarget ->
    Console.Error.WriteLine("Unrecognized target: " + someOtherTarget)
    Environment.Exit 2


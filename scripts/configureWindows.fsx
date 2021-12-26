#!/usr/bin/env fsharpi

open System
open System.IO

#r "System.Configuration"
open System.Configuration
#load "../InfraLib/Misc.fs"
#load "../InfraLib/Process.fs"
#load "../InfraLib/Git.fs"
open FSX.Infrastructure
open Process

let ConfigCommandCheck (commandNamesByOrderOfPreference: seq<string>) (exitIfNotFound: bool): Option<string> =
    let rec configCommandCheck currentCommandNamesQueue allCommands =
        match Seq.tryHead currentCommandNamesQueue with
        | Some currentCommand ->
            Console.Write (sprintf "checking for %s... " currentCommand)
            if not (Process.CommandWorksInShell currentCommand) then
                Console.WriteLine "not found"
                configCommandCheck (Seq.tail currentCommandNamesQueue) allCommands
            else
                Console.WriteLine "found"
                currentCommand |> Some
        | None ->
            Console.Error.WriteLine (sprintf "configure: error, please install %s" (String.Join(" or ", List.ofSeq allCommands)))
            if exitIfNotFound then
                Environment.Exit 1
                failwith "unreachable"
            else
                None

    configCommandCheck commandNamesByOrderOfPreference commandNamesByOrderOfPreference


let rootDir = DirectoryInfo(Path.Combine(__SOURCE_DIRECTORY__, ".."))

let initialConfigFile =
    match Misc.GuessPlatform() with
    | Misc.Platform.Windows ->
        // not using Mono anyway
        Map.empty
    | _ ->
        failwith "This configure script was only meant to be run on Windows, not Linux/macOS"

let buildTool: string =
    let programFiles = Environment.GetFolderPath Environment.SpecialFolder.ProgramFilesX86
    let msbuildPathPrefix = Path.Combine(programFiles, "Microsoft Visual Studio", "2019")

    let getMsBuildPath vsEdition =
        Path.Combine(msbuildPathPrefix, vsEdition, "MSBuild", "Current", "Bin", "MSBuild.exe")

    // FIXME: we should use vscheck.exe
    match
        ConfigCommandCheck
            [
                getMsBuildPath "Community"
                getMsBuildPath "Enterprise"
                getMsBuildPath "BuildTools"
            ]
            true
        with
    | Some theBuildTool -> theBuildTool
    | _ -> failwith "unreachable"


let prefix = DirectoryInfo(Misc.GatherOrGetDefaultPrefix(Misc.FsxOnlyArguments(), false, None))

if not (prefix.Exists) then
    let warning = sprintf "WARNING: prefix doesn't exist: %s" prefix.FullName
    Console.Error.WriteLine warning

let configFileLines =
    let toConfigFileLine (keyValuePair: System.Collections.Generic.KeyValuePair<string,string>) =
        sprintf "%s=%s" keyValuePair.Key keyValuePair.Value

    initialConfigFile.Add("Prefix", prefix.FullName)
                     .Add("BuildTool", buildTool)
    |> Seq.map toConfigFileLine

let buildConfigPath = Path.Combine(__SOURCE_DIRECTORY__, "build.config")
File.AppendAllLines(buildConfigPath, configFileLines |> Array.ofSeq)

let version = Misc.GetCurrentVersion rootDir
let repoInfo = Git.GetRepoInfo()

Console.WriteLine()
Console.WriteLine(sprintf
                      "\tConfiguration summary for fsx %s %s"
                      (version.ToString()) repoInfo)
Console.WriteLine()
Console.WriteLine(sprintf
                      "\t* Installation prefix: %s"
                      prefix.FullName)
Console.WriteLine()

Console.WriteLine "Configuration succeeded, you can now run `.\make.bat`"

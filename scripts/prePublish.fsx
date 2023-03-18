open System
open System.IO

#r "System.Configuration"
open System.Configuration

#load "../Fsdk/Misc.fs"
#load "../Fsdk/Process.fs"
#load "../Fsdk/Network.fs"
#load "../Fsdk/Git.fs"

open Fsdk
open Fsdk.Process

let rootDir = Path.Combine(__SOURCE_DIRECTORY__, "..") |> DirectoryInfo

let githubRef = Environment.GetEnvironmentVariable "GITHUB_REF"

if String.IsNullOrEmpty githubRef then
    Console.Error.WriteLine
        "This script is only meant to be launched within a CI pipeline"

    Environment.Exit 1

let versionConfigFileName = "version.config"

let versionConfigFile =
    Path.Combine(rootDir.FullName, versionConfigFileName) |> FileInfo

let AppendFullVersion fullVersion =
    let fullVersionVarAssignment =
        sprintf "%sFullVersion=%s" Environment.NewLine fullVersion

    File.AppendAllText(versionConfigFile.FullName, fullVersionVarAssignment)

let tagPrefix = "refs/tags/"

if githubRef.StartsWith tagPrefix then
    AppendFullVersion <| githubRef.Substring tagPrefix.Length
else
    let baseVersionTokenString = "BaseVersion="

    let rec ReadBaseVersion(lines: seq<string>) =
        match Seq.tryHead lines with
        | None -> None
        | Some line ->
            if line.StartsWith baseVersionTokenString then
                line.Substring baseVersionTokenString.Length |> Some
            else
                ReadBaseVersion <| Seq.tail lines

    let maybeBaseVersion =
        ReadBaseVersion <| File.ReadAllLines versionConfigFile.FullName

    match maybeBaseVersion with
    | None ->
        failwithf
            "%s file should contain a line with %s var set"
            versionConfigFile.Name
            baseVersionTokenString
    | Some baseVersion ->
        let nugetPush =
            Path.Combine(rootDir.FullName, "Tools", "nugetPush.fsx") |> FileInfo

        let fullVersion =
            Process
                .Execute(
                    {
                        Command = "dotnet"
                        Arguments =
                            sprintf
                                "fsi %s --output-version %s"
                                nugetPush.FullName
                                baseVersion
                    },
                    Echo.Off
                )
                .UnwrapDefault()
                .Trim()

        AppendFullVersion fullVersion

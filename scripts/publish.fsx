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

let tagPrefix = "refs/tags/"

let fullVersion =
    if githubRef.StartsWith tagPrefix then
        githubRef.Substring tagPrefix.Length
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
                Path.Combine(rootDir.FullName, "Tools", "nugetPush.fsx")
                |> FileInfo

            // to disable welcome msg, see https://stackoverflow.com/a/70493818/544947
            Environment.SetEnvironmentVariable("DOTNET_NOLOGO", "true")

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

            fullVersion

let Pack proj =
    Process
        .Execute(
            {
                Command = "dotnet"
                Arguments =
                    sprintf
                        "pack %s/%s.fsproj -property:PackageVersion=%s"
                        proj
                        proj
                        fullVersion
            },
            Echo.All
        )
        .UnwrapDefault()
    |> ignore

let projs = [ "fsxc"; "Fsdk"; "fsx" ]

for proj in projs do
    Pack proj

let defaultBranch = "master"
let branchPrefix = "refs/heads/"

if githubRef.StartsWith branchPrefix then
    if not(githubRef.StartsWith(sprintf "%s%s" branchPrefix defaultBranch)) then
        Console.WriteLine(
            sprintf
                "Branch different than '%s', skipping dotnet nuget push"
                defaultBranch
        )

        Environment.Exit 0
elif not(githubRef.StartsWith tagPrefix) then
    failwithf "Unexpected GITHUB_REF value: %s" githubRef

let nugetApiKeyVarName = "NUGET_API_KEY"
let nugetApiKey = Environment.GetEnvironmentVariable nugetApiKeyVarName

if String.IsNullOrEmpty nugetApiKey then
    Console.WriteLine(
        sprintf
            "Secret '%s' not set as env var, skipping dotnet nuget push"
            nugetApiKeyVarName
    )

    Environment.Exit 0

let githubEventName = Environment.GetEnvironmentVariable "GITHUB_EVENT_NAME"

match githubEventName with
| "push" ->
    let nugetApiSource = "https://api.nuget.org/v3/index.json"

    let NugetPush proj =
        Process
            .Execute(
                {
                    Command = "dotnet"
                    Arguments =
                        sprintf
                            "nuget push %s/nupkg/%s.%s.nupkg --api-key %s --source %s"
                            proj
                            proj
                            fullVersion
                            nugetApiKey
                            nugetApiSource
                },
                Echo.All
            )
            .UnwrapDefault()
        |> ignore

    for proj in projs do
        NugetPush proj

| null
| "" -> failwith "The env var for github event name should have a value"

| _ ->
    Console.WriteLine
        "Github event name is not 'push', skipping dotnet nuget push"

    Environment.Exit 0

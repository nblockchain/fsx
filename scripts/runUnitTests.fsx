#!/usr/bin/env fsx

open System
open System.IO
open System.Net
open System.Linq
open System.Diagnostics

#if LEGACY_FRAMEWORK
#r "System.Configuration"
open System.Configuration
#endif

#load "../Fsdk/Misc.fs"
#load "../Fsdk/Process.fs"
#load "../Fsdk/Git.fs"
#load "../Fsdk/Network.fs"

open Fsdk
open Fsdk.Process

let ScriptsDir = __SOURCE_DIRECTORY__ |> DirectoryInfo
let RootDir = Path.Combine(ScriptsDir.FullName, "..") |> DirectoryInfo
let TestDir = Path.Combine(RootDir.FullName, "test") |> DirectoryInfo
let NugetDir = Path.Combine(RootDir.FullName, ".nuget") |> DirectoryInfo
let NugetExe = Path.Combine(NugetDir.FullName, "nuget.exe") |> FileInfo
let NugetPackages = Path.Combine(RootDir.FullName, "packages") |> DirectoryInfo

#if !LEGACY_FRAMEWORK
let DotNetVersions = ["net8.0"; "net6.0"]
#else
let NunitVersion = "2.7.1"
#endif

let NugetScriptsPackagesDir() =
    let dir = Path.Combine(NugetDir.FullName, "packages") |> DirectoryInfo

    if not dir.Exists then
        Directory.CreateDirectory dir.FullName |> ignore

    dir

let MakeCheckCommand(commandName: string) =
    if not(Process.CommandWorksInShell commandName) then
        Console.Error.WriteLine(
            sprintf "%s not found, please install it first" commandName
        )

        Environment.Exit 1

let RunUnitTests() =
    Console.WriteLine "Running unit tests...\n"

    let testProjectName = "Fsdk.Tests"
#if !LEGACY_FRAMEWORK
    let testTarget =
        Path.Combine(
            RootDir.FullName,
            testProjectName,
            testProjectName + ".fsproj"
        )
        |> FileInfo
#else
    // so that we get file names in stack traces
    Environment.SetEnvironmentVariable("MONO_ENV_OPTIONS", "--debug")

    let testTargetDebug =
        Path.Combine(
            RootDir.FullName,
            testProjectName,
            "bin",
            "Debug",
            testProjectName + ".dll"
        )
        |> FileInfo

    let testTargetRelease =
        Path.Combine(
            RootDir.FullName,
            testProjectName,
            "bin",
            "Release",
            testProjectName + ".dll"
        )
        |> FileInfo

    let testTarget =
        if testTargetDebug.Exists then
            testTargetDebug
        else
            testTargetRelease

    if not testTarget.Exists then
        failwithf "File not found: %s" testTarget.FullName
#endif


    let runnerCommand =
#if !LEGACY_FRAMEWORK
        {
            Command = "dotnet"
            Arguments = "test " + testTarget.FullName
        }
#else
        match Misc.GuessPlatform() with
        | Misc.Platform.Linux ->
            let nunitCommand = "nunit-console"
            MakeCheckCommand nunitCommand

            {
                Command = nunitCommand
                Arguments = testTarget.FullName
            }
        | _ ->
            let nunitVersion = "2.7.1"
            let pkgOutputDir = NugetScriptsPackagesDir()

            Network.InstallNugetPackage
                NugetExe
                pkgOutputDir
                "NUnit.Runners"
                (Some nunitVersion)
                Echo.All
            |> ignore

            {
                Command =
                    Path.Combine(
                        NugetScriptsPackagesDir().FullName,
                        sprintf "NUnit.Runners.%s" nunitVersion,
                        "tools",
                        "nunit-console.exe"
                    )
                Arguments = testTarget.FullName
            }
#endif

    Process
        .Execute(runnerCommand, Echo.All)
        .UnwrapDefault()
    |> ignore

RunUnitTests()

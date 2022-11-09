#!/usr/bin/env fsx
open System
open System.IO
open System.Linq

#r "System.Configuration"
open System.Configuration

#load "../InfraLib/Misc.fs"
#load "../InfraLib/Process.fs"

open FSX.Infrastructure
open Process

let sourceDir = DirectoryInfo __SOURCE_DIRECTORY__

let sample =
    Path.Combine(sourceDir.FullName, "testProcessSample.fsx") |> FileInfo

let mutable retryCount = 0

let command =
#if !LEGACY_FRAMEWORK
    "dotnet"
#else
    if Misc.GuessPlatform() = Misc.Platform.Windows then
        // HACK: we should call fsx here but then we would get this problem in
        // the tests: error FS0193: The process cannot access the file 'D:\a\fsx\fsx\test\bin\FSharp.Core.dll' because it is being used by another process.
        // so then we gotta be pragmatic here, and hope that when we migrate to
        // .NET6 (using dotnet fsi and a global/GAC FSharp.Core?) it's fixed
        let vswherePath =
            Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86
                ),
                "Microsoft Visual Studio",
                "Installer",
                "vswhere.exe"
            )

        Process
            .Execute(
                {
                    Command = vswherePath
                    Arguments = "-find **\\fsi.exe"
                },
                Echo.Off
            )
            .UnwrapDefault()
            .Split(
                Array.singleton Environment.NewLine,
                StringSplitOptions.RemoveEmptyEntries
            )
            .First()
    else
        // FIXME: extract PREFIX from build.config instead of assuming default
        "/usr/local/bin/fsx"
#endif

while (retryCount < 20) do //this is a stress test
    let procResult =
        Process.Execute(
            {
                Command = command
#if !LEGACY_FRAMEWORK
                Arguments = sprintf "fsi %s" sample.FullName
#else
                Arguments = sample.FullName
#endif
            },
            Echo.Off
        )

    let actual =
        (procResult
            .UnwrapDefault()
            .Replace(Environment.NewLine, "-"))

    let expected = "foo-bar-baz-"

    if (actual <> expected) then
        Console.Error.WriteLine(
            sprintf
                "Stress test failed, got `%s`, should have been `%s`"
                actual
                expected
        )

        Environment.Exit 1

    retryCount <- retryCount + 1

Console.WriteLine "Success"
Environment.Exit 0

#!/usr/bin/env fsx
open System
open System.IO

#r "System.Configuration"
open System.Configuration

#load "../Fsdk/Misc.fs"
#load "../Fsdk/Process.fs"

open Fsdk
open Fsdk.Process

let mutable retryCount = 0

while (retryCount < 20) do //this is a stress test
    let procResult =
        Process.Execute(
            {
                Command = "fsharpi"
                Arguments = "test/testProcessConcurrencySample.fsx"
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

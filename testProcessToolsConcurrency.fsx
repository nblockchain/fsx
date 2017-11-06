#!/usr/bin/env fsharpi
open System
open System.IO

open System
open System.IO
#load "Infra.fs"
open FSX.Infrastructure

let mutable retryCount = 0
while (retryCount < 20) do //this is a stress test
    let procResult = Process.Execute("fsharpi testProcessToolsConcurrencySample.fsx", false, true)
    let actual = Process.ToString(procResult.Output).Replace(Environment.NewLine,"-")
    let expected = "foo-bar-baz-"
    if (actual <> expected) then
        Console.Error.WriteLine (sprintf "Stress test failed, got `%s`, should have been `%s`" actual expected)
        Environment.Exit 1

Console.WriteLine "Success"
Environment.Exit 0


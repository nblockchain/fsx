#!/usr/bin/env fsx
open System
open System.IO

#r "System.Configuration"
#load "InfraLib/Misc.fs"
#load "InfraLib/Process.fs"
open FSX.Infrastructure
open Process

let mutable retryCount = 0
while (retryCount < 20) do //this is a stress test
    let procResult = Process.Execute({ Command = "fsharpi"; Arguments = "testProcessConcurrencySample.fsx" }, Echo.Off)
    let actual = (procResult.Output.ToString().Replace(Environment.NewLine,"-"))
    let expected = "foo-bar-baz-"
    if (actual <> expected) then
        Console.Error.WriteLine (sprintf "Stress test failed, got `%s`, should have been `%s`" actual expected)
        Environment.Exit 1
    retryCount <- retryCount + 1

Console.WriteLine "Success"
Environment.Exit 0


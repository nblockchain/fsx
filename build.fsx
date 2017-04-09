#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq

#load "Infra.fs"
open FSX.Infrastructure

Console.WriteLine("Checking if all .fsx scripts build")

let allFsxScripts = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.fsx", SearchOption.AllDirectories)
let fsxScripts = allFsxScripts.Where(fun scriptPath -> scriptPath.EndsWith("fsx.fsx"))
if (fsxScripts.Count() > 1) then
    Console.Error.WriteLine("More than one fsx.fsx file found, please just leave one")
    Environment.Exit(1)
if (fsxScripts.Count() = 0) then
    Console.Error.WriteLine("fsx.fsx script not found")
    Environment.Exit(1)
let fsxLocation = fsxScripts.Single()

let buildFsxScript(script: string, soFar: bool): bool =
    if (script = null) then
        raise(ArgumentNullException("script"))

    let currentDir = Directory.GetCurrentDirectory()
    Console.WriteLine(sprintf "Building %s" script)
    let procResult = Process.Execute(sprintf "%s -k %s" fsxLocation script, false, false)

    let success = match procResult.ExitCode with
                  | 0 -> true
                  | _ -> false

    Console.WriteLine()

    (success && soFar)

let rec buildAll(scripts: string list, soFar: bool): bool =
    match scripts with
    | [] -> soFar
    | script::tail ->
        let sofarPlusOne = buildFsxScript(script, soFar)
        buildAll(tail, sofarPlusOne)

let scripts = List.ofArray (allFsxScripts)
let allCompile = buildAll(scripts, true)

if (allCompile) then
    Console.WriteLine("Success")
    Environment.Exit(0)
else
    Console.WriteLine("Some script(s) had errors")
    Environment.Exit(1)

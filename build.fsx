#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq

#load "Infra.fs"
open FSX.Infrastructure

let buildFsxScript(script: string, sofar: bool) : bool =
    if (script = null) then
        raise(new ArgumentNullException("script"))

    let currentDir = Directory.GetCurrentDirectory()
    let fsx = Path.Combine(currentDir, "fsx.fsx")
    Console.WriteLine(sprintf "Building %s" script)
    let procResult = Process.Execute(sprintf "%s -k %s" fsx script, false, false)

    let success = match procResult.ExitCode with
                  | 0 -> true
                  | _ -> false

    Console.WriteLine()

    (success && sofar)

let rec buildAll(scripts: string list, sofar: bool) : bool =
    match scripts with
    | [] -> sofar
    | script::tail ->
        let sofarPlusOne = buildFsxScript(script, sofar)
        buildAll(tail, sofarPlusOne)

Console.WriteLine("Checking if all .fsx scripts build")

let allFsxScripts = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.fsx", SearchOption.AllDirectories)
let scripts = List.ofArray (allFsxScripts)
let allCompile = buildAll(scripts, true)

if (allCompile) then
    Console.WriteLine("Success")
    Environment.Exit(0)
else
    Console.WriteLine("Some script(s) had errors")
    Environment.Exit(1)

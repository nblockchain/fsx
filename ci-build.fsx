#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq

#r "System.Configuration"
#load "InfraLib/Misc.fs"
#load "InfraLib/Process.fs"
open FSX.Infrastructure
open Process

Console.WriteLine("Checking if all .fsx scripts build")

let fsxCompiler = "fsxc.fsx"

let allFsxScripts = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.fsx", SearchOption.AllDirectories)
let fsxScripts = allFsxScripts.Where(fun scriptPath -> FileInfo(scriptPath).Name = fsxCompiler)
if (fsxScripts.Count() > 1) then
    Console.Error.WriteLine(sprintf "More than one %s file found, please just leave one" fsxCompiler)
    Environment.Exit(1)
if (fsxScripts.Count() = 0) then
    Console.Error.WriteLine(sprintf "%s script not found" fsxCompiler)
    Environment.Exit(1)
let fsxLocation = fsxScripts.Single()

let buildFsxScript (script: string) (soFar: bool): bool =
    if (script = null) then
        raise(ArgumentNullException("script"))

    let currentDir = Directory.GetCurrentDirectory()
    Console.WriteLine(sprintf "Building %s" script)
    let procResult = Process.Execute({ Command = fsxLocation; Arguments = sprintf "-k %s" script }, Echo.OutputOnly)

    let success = match procResult.ExitCode with
                  | 0 -> true
                  | _ -> false

    Console.WriteLine()

    (success && soFar)

let rec buildAll(scripts: string list) (soFar: bool): bool =
    match scripts with
    | [] -> soFar
    | script::tail ->
        let sofarPlusOne = buildFsxScript script soFar
        buildAll tail sofarPlusOne

let scripts = List.ofArray (allFsxScripts)
let allCompile = buildAll scripts true

if (allCompile) then
    Console.WriteLine("Success")
    Environment.Exit(0)
else
    Console.WriteLine("Some script(s) had errors")
    Environment.Exit(1)

#!/usr/bin/env fsharpi

open System
open System.IO

#load "Infra.fs"
open Fsx.Infrastructure


let PrintUsage () =
    Console.WriteLine("Usage: ./fsx.fsx  [OPTION]... yourscript.fsx")
    Console.WriteLine()
    Console.WriteLine("Options")
    Console.WriteLine("  -c, --compile     Only compile, don't run (ideal for CI build scripts)")

let args = Util.FsxArguments()
if (args.Length = 0 || (args.Length = 1 && args.[0] = "--help")) then
    PrintUsage()
    Environment.Exit(1)

let rec FindScript(args: string list, script: Option<FileInfo>): Option<FileInfo> =
    match args with
    | [] -> script
    | arg::tail ->
        let isScript = arg.EndsWith(".fsx")
        let isCommand = (arg = "-c" || arg = "--compile")

        if ((not isScript) && (not isCommand)) then
            failwith (sprintf "Argument not recognized: %s. Only commands or scripts ending with .fsx allowed" arg)

        match script with
        | None ->
            if (isScript) then
                FindScript(tail, Some(new FileInfo(arg)))
            else
                FindScript(tail, None)
        | Some(alreadyScript) ->
            if (isScript) then
                failwith (sprintf "Only one .fsx script allowed")
            FindScript(tail, script)

let maybeScript = FindScript(args, None)
match maybeScript with
| None ->
    Console.Error.WriteLine("At least one .fsx script is required as input. Use --help for info.")
    Environment.Exit(1)
| Some(script) ->
    Console.WriteLine("Going to run " + script.FullName)
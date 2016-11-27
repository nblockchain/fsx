#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq

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

let BuildFsxScript(script: FileInfo) : bool =
    // TODO: this is good enough to catch syntax errors, but doesn't catch semantic errors;
    // for that we would need to get the contents inside an [<EntryPoint>] func, and compile with fsharpc

    if (script = null) then
        raise(new ArgumentNullException("script"))

    Console.WriteLine("Building {0}", script)
    let bakFile = script.FullName.Replace(".fsx", ".fsx.bak")
    File.Copy(script.FullName, bakFile, true)
    let contents = File.ReadAllText(script.FullName)

    let lines = contents.Split([| Environment.NewLine |], StringSplitOptions.None)

    let newContents =
        let exitCall = "System.Environment.Exit(0)"
        if (lines.Any() && lines.[0].StartsWith("#")) then
            let allLinesExceptFirst = Array.skip 1 lines
            let scriptMinusShebang = String.Join(Environment.NewLine, allLinesExceptFirst)
            String.Format("{0}{1}{2}{1}{3}", lines.[0], Environment.NewLine, exitCall, scriptMinusShebang)
        else
            String.Format("{0}{1}{2}", exitCall, Environment.NewLine, contents)

    let exitCode =
        try
            File.WriteAllText(script.FullName, newContents)
            let exitCode,_,_ = Process.Execute("fsharpi " + script.FullName, true, false)
            exitCode

        finally
            File.Delete(script.FullName)
            File.Move(bakFile, script.FullName)

    let success =
        match exitCode with
        | 0 -> true
        | _ -> false

    if not (success) then
        Console.Error.WriteLine("Build failure")
    Console.WriteLine()

    success

let maybeScript = FindScript(args, None)
match maybeScript with
| None ->
    Console.Error.WriteLine("At least one .fsx script is required as input. Use --help for info.")
    Environment.Exit(1)
| Some(script) ->
    if (BuildFsxScript(script)) then
        Environment.Exit(0)
    else
        Environment.Exit(1)
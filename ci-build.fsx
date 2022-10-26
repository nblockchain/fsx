#!/usr/bin/env fsx

open System
open System.IO
open System.Linq

#r "System.Configuration"
open System.Configuration

#load "InfraLib/Misc.fs"
#load "InfraLib/Process.fs"

open FSX.Infrastructure
open Process

let fsxRootDir = __SOURCE_DIRECTORY__ |> DirectoryInfo

let rec FindFsxc(nestedCall: bool) : FileInfo =
    let fsxCompiler = "fsxc.exe"

    let fsxcBinDir = Path.Combine(__SOURCE_DIRECTORY__, "fsxc", "bin")

    let findFsxcExeFiles() =
        Directory.GetFiles(fsxcBinDir, fsxCompiler, SearchOption.AllDirectories)

    if not(Directory.Exists fsxcBinDir) || not(findFsxcExeFiles().Any()) then
        if nestedCall then
            Console.Error.WriteLine(
                sprintf "'%s' compilation didn't work?" fsxCompiler
            )

            Environment.Exit 1

        let prevCurrentDir = Directory.GetCurrentDirectory()
        Directory.SetCurrentDirectory fsxRootDir.FullName

        let configureProc =
            Process.Execute(
                {
                    Command = "./configure.sh"
                    Arguments = String.Empty
                },
                Echo.All
            )

        if configureProc.ExitCode <> 0 then
            Environment.Exit 1
            failwith "Unreachable"

        let makeProc =
            Process.Execute(
                {
                    Command = "make"
                    Arguments = String.Empty
                },
                Echo.All
            )

        if makeProc.ExitCode <> 0 then
            Environment.Exit 1
            failwith "Unreachable"

        Directory.SetCurrentDirectory prevCurrentDir

        FindFsxc true

    elif findFsxcExeFiles().Count() > 1 then

        Console.Error.WriteLine(
            sprintf
                "More than one %s file found (%s), please just leave one"
                fsxCompiler
                (String.Join(", ", findFsxcExeFiles()))
        )

        Environment.Exit 1
        failwith "Unreachable"

    else
        findFsxcExeFiles().Single() |> FileInfo

let fsxLocation = FindFsxc false

Console.WriteLine("Checking if all .fsx scripts build")

let fsxScripts =
    Directory.GetFiles(
        Directory.GetCurrentDirectory(),
        "*.fsx",
        SearchOption.AllDirectories
    )

let buildFsxScript (script: string) (soFar: bool) : bool =
    if (script = null) then
        raise <| ArgumentNullException("script")

    Console.WriteLine(sprintf "Building %s" script)

    let procResult =
        Process.Execute(
            {
                Command = fsxLocation.FullName
                Arguments = sprintf "-k %s" script
            },
            Echo.OutputOnly
        )

    let success =
        match procResult.ExitCode with
        | 0 -> true
        | _ -> false

    Console.WriteLine()

    (success && soFar)

let rec buildAll (scripts: list<string>) (soFar: bool) : bool =
    match scripts with
    | [] -> soFar
    | script :: tail ->
        let sofarPlusOne = buildFsxScript script soFar
        buildAll tail sofarPlusOne

let scripts = List.ofArray fsxScripts
let allCompile = buildAll scripts true

if allCompile then
    Console.WriteLine("Success")
    Environment.Exit(0)
else
    Console.WriteLine("Some script(s) had errors")
    Environment.Exit(1)

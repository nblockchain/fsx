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
let fsxTestsDir = Path.Combine(fsxRootDir.FullName, "test") |> DirectoryInfo

let rec FindFsxc(nestedCall: bool) : bool * FileInfo =
#if !LEGACY_FRAMEWORK
    let fsxCompiler = "fsxc.dll"
#else
    let fsxCompiler = "fsxc.exe"
#endif

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

        configureProc.UnwrapDefault() |> ignore<string>

        let makeProc =
            Process.Execute(
                {
                    Command = "make"
                    Arguments = String.Empty
                },
                Echo.All
            )

        match makeProc.Result with
        | Error _ ->
            Console.WriteLine()
            Console.Out.Flush()
            failwith "Compilation failed"
        | _ -> ()

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
        nestedCall, findFsxcExeFiles().Single() |> FileInfo

let compilationWasNeeded, fsxLocation = FindFsxc false

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

    let proc =
        Process.Execute(
            {
#if !LEGACY_FRAMEWORK
                Command = "dotnet"
                Arguments = sprintf "%s -k %s" fsxLocation.FullName script
#else
                Command = fsxLocation.FullName
                Arguments = sprintf "-k %s" script
#endif
            },
            Echo.OutputOnly
        )

    let success =
        match proc.Result with
        | Success _ -> true
        | WarningsOrAmbiguous output ->
            output.PrintToConsole()
            Console.WriteLine()
            Console.Out.Flush()
            failwith "Unexpected 'fsx' output ^ (with warnings?)"
        | _ -> false

    Console.WriteLine()

    (success && soFar)

let rec buildAll (scripts: list<string>) (soFar: bool) : bool =
    match scripts with
    | [] -> soFar
    | script :: tail ->
        let scriptFile = FileInfo script

        let binFolder =
            sprintf
                "%c%s%c"
                Path.DirectorySeparatorChar
                "bin"
                Path.DirectorySeparatorChar

        let skip =
            // if compilation was needed, it's likely we are running under a
            // repo which is not fsx itself, so we don't want to compile fsx's
            // test scripts (because they have dependencies)
            if compilationWasNeeded
               && scriptFile.Directory.FullName = fsxTestsDir.FullName then
                true
            elif scriptFile.FullName.Contains binFolder then
                true
            else
                false

        if skip then
            Console.WriteLine(sprintf "Skipping %s" script)
            buildAll tail soFar
        else
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

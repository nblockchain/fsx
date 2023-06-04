#!/usr/bin/env -S dotnet fsi

open System
open System.IO

#if LEGACY_FRAMEWORK
Console.Error.WriteLine "This script is only compatible with .NET6 or higher"
Environment.Exit 1
#else

#load "../Fsdk/Misc.fs"
open Fsdk

let args = Misc.FsxOnlyArguments()

if args.Length > 1 then
    Console.Error.WriteLine
        "Can only pass one argument: --force (for when deciding not to do a dry-run)"

    Environment.Exit 1

let dryRun =
    if args.Length = 0 then
        Console.WriteLine "No arguments detected, performing dry-run"
        true
    else if args.[0] <> "--force" then
        Console.Error.WriteLine
            "Can only pass one flag: --force (for when deciding not to do a dry-run)"

        Environment.Exit 2
        failwith "Unreachable"
    else
        raise <| NotImplementedException "Not yet implemented buddy"
        false

let currentDir = Directory.GetCurrentDirectory() |> DirectoryInfo

let illegalCharsInExFat =
    [
        '\\'
        '/'
        '*'
        '?'
        '"'
        '<'
        '>'
        '|'
    ]

let CheckName (fileOrDirName: string) (fullName: string) =
    for illegalChar in illegalCharsInExFat do
        if fileOrDirName.Contains illegalChar then
            Console.WriteLine(
                sprintf "Illegal char (for exFAT) found in %s" fullName
            )

    let allChars: seq<char> = fileOrDirName

    if not(Seq.forall (fun (aChar: char) -> Char.IsAscii aChar) allChars) then
        Console.WriteLine(
            sprintf "Illegal char (nonASCII) found in %s" fullName
        )

let rec Rename(dir: DirectoryInfo) : unit =
    CheckName dir.Name dir.FullName

    for subDir in dir.EnumerateDirectories() do
        Rename subDir

    for file in dir.EnumerateFiles() do
        CheckName file.Name file.FullName

Rename currentDir
#endif

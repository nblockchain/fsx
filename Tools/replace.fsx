#!/usr/bin/env fsharpi

open System
open System.IO

#r "System.Configuration"
open System.Configuration

#load "../Fsdk/Misc.fs"
open Fsdk

let args = Misc.FsxOnlyArguments()

let errTooManyArgs =
    "Can only pass two arguments, with optional flag: replace.fsx -f=a.b oldstring newstring"

let note =
    "NOTE: by default, some kind of files/folders will be excluded, e.g.: .git, *.dll, *.png, ..."

if args.Length > 3 then
    Console.Error.WriteLine errTooManyArgs
    Console.WriteLine note
    Environment.Exit 1
elif args.Length < 2 then
    Console.Error.WriteLine
        "Need to pass two arguments: replace.fsx oldstring newstring"

    Console.WriteLine note
    Environment.Exit 1

let firstArg = args.[0]

let particularFile =
    if firstArg.StartsWith "--file=" || firstArg.StartsWith "-f=" then
        let file = firstArg.Substring(firstArg.IndexOf("=") + 1) |> FileInfo

        if not file.Exists then
            failwithf "File '%s' doesn't exist" file.FullName

        file |> Some
    else
        if args.Length = 3 then
            Console.Error.WriteLine errTooManyArgs
            Console.WriteLine note
            Environment.Exit 1
            failwith "Unreachable"

        None

match particularFile with
| None ->
    let startDir = DirectoryInfo(Directory.GetCurrentDirectory())
    let oldString, newString = args.[0], args.[1]
    Misc.ReplaceTextInDir startDir oldString newString
| Some file ->
    let oldString, newString = args.[1], args.[2]
    Misc.ReplaceTextInFile file oldString newString

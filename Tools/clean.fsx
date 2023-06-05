#!/usr/bin/env -S dotnet fsi

open System
open System.IO

#r "System.Configuration"
open System.Configuration

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
        false

let currentDir = Directory.GetCurrentDirectory() |> DirectoryInfo

let rec CanBeDeleted(dir: DirectoryInfo) : bool =
    match dir.Name with
    | "packages" -> dir.Parent.Parent.FullName = currentDir.FullName
    | "bin"
    | "obj" -> dir.Parent.Parent.Name = "src"
    | _ -> false

let rec Clean(dir: DirectoryInfo) =
    let subDirs = dir.EnumerateDirectories()

    for subDir in subDirs do
        if CanBeDeleted subDir then
            if dryRun then
                Console.WriteLine(
                    sprintf "Dir %s can be deleted" subDir.FullName
                )
            else
                Console.WriteLine(sprintf "Deleting dir %s" subDir.FullName)
                subDir.Delete true
        else
            Clean subDir

Clean currentDir

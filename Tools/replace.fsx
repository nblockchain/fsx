#!/usr/bin/env fsharpi

open System
open System.IO
open System.Text

#r "System.Configuration"
open System.Configuration
#load "../InfraLib/Misc.fs"

open FSX.Infrastructure

let rec ReplaceInDir (dir: DirectoryInfo) (oldString: string) (newString: string) =
    let ReplaceInFile (file: FileInfo) (oldString: string) (newString: string) =
        let HasUtf8Bom (file: FileInfo) =
            let fileContentInBytes = File.ReadAllBytes file.FullName
            fileContentInBytes.Length > 2 && fileContentInBytes.[..2] = [|0xEFuy; 0xBBuy; 0xBFuy|]

        let oldText = File.ReadAllText file.FullName
        let newText = oldText.Replace(oldString, newString)
        if newText <> oldText then
            File.WriteAllText(file.FullName, newText, UTF8Encoding(HasUtf8Bom file))

    for file in dir.GetFiles() do
        if (file.Extension.ToLower() <> "dll") &&
           (file.Extension.ToLower() <> "exe") &&
           (file.Extension.ToLower() <> "png") then
            ReplaceInFile file oldString newString

    for subFolder in dir.GetDirectories() do
        if subFolder.Name <> ".git" then
            ReplaceInDir subFolder oldString newString

let args = Misc.FsxArguments()
let note = "NOTE: by default, some kind of files/folders will be excluded, e.g.: .git, *.dll, *.png, ..."
if args.Length > 2 then
    Console.Error.WriteLine "Can only pass two arguments: replace.fsx oldstring newstring"
    Console.WriteLine note
    Environment.Exit 1
elif args.Length < 2 then
    Console.Error.WriteLine "Need to pass two arguments: replace.fsx oldstring newstring"
    Console.WriteLine note
    Environment.Exit 1

let oldString = args.[0]
let newString = args.[1]

let startDir = DirectoryInfo (Directory.GetCurrentDirectory())

ReplaceInDir startDir oldString newString


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

let CheckTimes(filesAndSubDirs: seq<FileSystemInfo>) =
    let exFatEarliestAllowedTime = DateTime(1980, 1, 1, 0, 0, 0)

    let checkTimeStampIsCorrect (entry: FileSystemInfo) (date: DateTime) =
        let nugetMagicFolderMagicDate = DateTime(1979, 12, 31, 16, 0, 0)

        if date.ToUniversalTime() <> nugetMagicFolderMagicDate
           && date < exFatEarliestAllowedTime then
            if dryRun then
                Console.Error.WriteLine(
                    sprintf
                        "Illegal timestamp (for exFAT) found in %s"
                        entry.FullName
                )

                true
            else
                false
        else
            true

    for entry in filesAndSubDirs do
        if not(checkTimeStampIsCorrect entry entry.CreationTime) then
            entry.CreationTime <- exFatEarliestAllowedTime

        if not(checkTimeStampIsCorrect entry entry.CreationTimeUtc) then
            entry.CreationTimeUtc <- exFatEarliestAllowedTime

        if not(checkTimeStampIsCorrect entry entry.LastAccessTime) then
            entry.LastAccessTime <- exFatEarliestAllowedTime

        if not(checkTimeStampIsCorrect entry entry.LastAccessTimeUtc) then
            entry.LastAccessTimeUtc <- exFatEarliestAllowedTime

        if not(checkTimeStampIsCorrect entry entry.LastWriteTime) then
            entry.LastWriteTime <- exFatEarliestAllowedTime

        if not(checkTimeStampIsCorrect entry entry.LastWriteTimeUtc) then
            entry.LastWriteTimeUtc <- exFatEarliestAllowedTime

let CheckNames(filesAndSubDirs: seq<FileSystemInfo>) =
    let rec addToMap
        (entries: seq<FileSystemInfo>)
        (accMap: Map<string, seq<FileSystemInfo>>)
        : Map<string, seq<FileSystemInfo>> =
        match Seq.tryHead entries with
        | None -> accMap
        | Some head ->
            let keyForEntry = head.Name.ToLower()

            let newMap =
                match Map.tryFind keyForEntry accMap with
                | None -> Map.add keyForEntry (Seq.singleton head) accMap
                | Some existingEntries ->
                    Map.add
                        keyForEntry
                        (Seq.append existingEntries (Seq.singleton head))
                        accMap

            addToMap (Seq.tail entries) newMap

    let namesMap = addToMap filesAndSubDirs Map.empty

    for KeyValue(_key, value) in namesMap do
        match Seq.length value with
        | 1 -> ()
        | 0 -> failwith "Something went wrong..."
        | _ ->
            Console.Error.WriteLine
                "Some file system entries were found whose name only differs in case (illegal in exFAT):"

            for entry in value do
                Console.Error.WriteLine("* " + entry.FullName)

            Console.Error.WriteLine()

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
    let Separate
        (entries: seq<FileSystemInfo>)
        : seq<DirectoryInfo> * seq<FileInfo> =
        let dirs =
            seq {
                for entry in entries do
                    if Directory.Exists entry.FullName then
                        yield DirectoryInfo entry.FullName
            }

        let files =
            seq {
                for entry in entries do
                    if File.Exists entry.FullName then
                        yield FileInfo entry.FullName
            }

        dirs, files

    CheckName dir.Name dir.FullName

    let allEntries = dir.EnumerateFileSystemInfos()

    let subDirs, files = Separate allEntries

    CheckNames allEntries

    CheckTimes allEntries

    for subDir in subDirs do
        if not(isNull subDir.LinkTarget) then
            Console.WriteLine(
                sprintf
                    "Skipping link %s (if using robocopy, exclude them via /xj)"
                    subDir.FullName
            )
        else
            Rename subDir

    for file in files do
        CheckName file.Name file.FullName

try
    Rename currentDir
with
| :? UnauthorizedAccessException ->
    Console.Error.WriteLine
        "Encountered an access-denied error, did you run with root/Administrator privileges?"

    exit 3
#endif

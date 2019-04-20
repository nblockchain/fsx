#!/usr/bin/env fsharpi

open System
open System.IO
#r "System.Configuration"
open System.Configuration
#load "../InfraLib/MiscTools.fs"
#load "../InfraLib/ProcessTools.fs"
open FSX.Infrastructure
open ProcessTools

let IsStableRevision revision =
    (int revision % 2) = 0

let Bump(toStable: bool): Version*Version =
    let rootDir = DirectoryInfo(Path.Combine(__SOURCE_DIRECTORY__, ".."))
    let fullVersion = MiscTools.GetCurrentVersion(rootDir)
    let androidVersion = fullVersion.MinorRevision

    if toStable && IsStableRevision androidVersion then
        failwith "bump script expects you to be in unstable version currently, but we found a stable"
    if (not toStable) && (not (IsStableRevision androidVersion)) then
        failwith "sanity check failed, post-bump should happen in a stable version"

    let newFullVersion,newVersion =
        if MiscTools.FsxArguments().Length > 0 then
            if MiscTools.FsxArguments().Length > 1 then
                Console.Error.WriteLine "Only one argument supported, not more"
                Environment.Exit 1
                failwith "Unreachable"
            else
                let full = Version(MiscTools.FsxArguments().Head)
                full,full.MinorRevision
        else
            let newVersion = androidVersion + 1s
            let full = Version(sprintf "%i.%i.%i.%i"
                                       fullVersion.Major
                                       fullVersion.Minor
                                       fullVersion.Build
                                       newVersion)
            full,newVersion

    let replaceScript = Path.Combine(__SOURCE_DIRECTORY__, "replace.fsx")
    ProcessTools.SafeExecute ({ Command = replaceScript
                                Arguments = sprintf "%s %s"
                                                    (fullVersion.ToString())
                                                    (newFullVersion.ToString()) },
                              Echo.Off) |> ignore

    // this code is weird, I know, but it's to avoid replace.fsx to change this script itself!
    let pluralSuffix = "s"
    let artifactsExpiry =
        if toStable then
            sprintf "50day%s 50year%s" pluralSuffix pluralSuffix
        else
            sprintf "50year%s 50day%s" pluralSuffix pluralSuffix
    ProcessTools.SafeExecute ({ Command = replaceScript
                                Arguments = artifactsExpiry },
                              Echo.Off) |> ignore

    fullVersion,newFullVersion


let GitCommit (fullVersion: Version) (newFullVersion: Version) =
    ProcessTools.SafeExecute ({ Command = "git"
                                Arguments = "add version.config" },
                              Echo.Off) |> ignore
    ProcessTools.SafeExecute ({ Command = "git"
                                Arguments = "add snap/snapcraft.yaml" },
                              Echo.Off) |> ignore
    ProcessTools.SafeExecute ({ Command = "git"
                                Arguments = "add InfraLib/AssemblyInfo.fs" },
                              Echo.Off) |> ignore
    ProcessTools.SafeExecute ({ Command = "git"
                                Arguments = "add .gitlab-ci.yml" },
                              Echo.Off) |> ignore

    let commitMessage = sprintf "Bump version: %s -> %s" (fullVersion.ToString()) (newFullVersion.ToString())
    let finalCommitMessage =
        if IsStableRevision fullVersion.MinorRevision then
            sprintf "(Post)%s" commitMessage
        else
            commitMessage
    ProcessTools.SafeExecute ({ Command = "git"
                                Arguments = sprintf "commit -m \"%s\"" finalCommitMessage },
                              Echo.Off) |> ignore

let GitTag (newFullVersion: Version) =
    if not (IsStableRevision newFullVersion.MinorRevision) then
        failwith "something is wrong, this script should tag only even(stable) minorRevisions, not odd(unstable) ones"

    ProcessTools.Execute ({ Command = "git"
                            Arguments = sprintf "tag --delete %s" (newFullVersion.ToString()) },
                          Echo.Off) |> ignore
    ProcessTools.SafeExecute ({ Command = "git"
                                Arguments = sprintf "tag %s" (newFullVersion.ToString()) },
                              Echo.Off) |> ignore

Console.WriteLine "Bumping..."
let fullUnstableVersion,newFullStableVersion = Bump true
GitCommit fullUnstableVersion newFullStableVersion
GitTag newFullStableVersion

Console.WriteLine (sprintf "Version bumped to %s, release binaries now and press key when you finish."
                           (newFullStableVersion.ToString()))
Console.Read() |> ignore

Console.WriteLine "Post-bumping..."
let fullStableVersion,newFullUnstableVersion = Bump false
GitCommit fullStableVersion newFullUnstableVersion

Console.WriteLine (sprintf "Version bumping finished. Remember to push via `git push <remote> <branch> %s`"
                           (newFullStableVersion.ToString()))

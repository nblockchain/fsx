#!/usr/bin/env fsharpi

open System
open System.IO

#r "System.Configuration"
#load "InfraLib/Misc.fs"
#load "InfraLib/Process.fs"
open FSX.Infrastructure
open Process

Console.Write "checking for F# compiler... "
let fsharpCompiler = "fsharpc"
let fsharpcWhich = Process.Execute({ Command = "which"; Arguments = fsharpCompiler }, Echo.Off)
if (fsharpcWhich.ExitCode <> 0) then
    Console.Error.WriteLine("not found")
    Console.Error.WriteLine(sprintf "configuration failed, please install \"%s\"" fsharpCompiler)
    Environment.Exit(1)
else
    Console.WriteLine("found")

let rec private GatherOrGetDefaultPrefix(args: string list, previousIsPrefixArg: bool, prefixSet: Option<string>): string =
    let GatherPrefix(newPrefix: string): Option<string> =
        match prefixSet with
        | None -> Some(newPrefix)
        | _ -> failwith ("prefix argument duplicated")

    let prefixArgWithEquals = "--prefix="
    match args with
    | [] ->
        match prefixSet with
        | None -> "/usr/local"
        | Some(prefix) -> prefix
    | head::tail ->
        if (previousIsPrefixArg) then
            GatherOrGetDefaultPrefix(tail, false, GatherPrefix(head))
        else if head = "--prefix" then
            GatherOrGetDefaultPrefix(tail, true, prefixSet)
        else if head.StartsWith(prefixArgWithEquals) then
            GatherOrGetDefaultPrefix(tail, false, GatherPrefix(head.Substring(prefixArgWithEquals.Length)))
        else
            failwith (sprintf "argument not recognized: %s" head)

let prefix = DirectoryInfo(GatherOrGetDefaultPrefix(Misc.FsxArguments(), false, None))

if not (prefix.Exists) then
    let warning = sprintf "WARNING: prefix doesn't exist: %s" prefix.FullName
    Console.Error.WriteLine (warning)

File.WriteAllText(Path.Combine(__SOURCE_DIRECTORY__, "build.config"),
                  sprintf "Prefix=%s" prefix.FullName)

let versionContents = File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "version.config"))
let version = versionContents.Substring("Version=".Length).Trim()

let GetRepoInfo()=
    let rec GetBranchFromGitBranch(outchunks: list<string>)=
        match outchunks with
        | [] -> failwith "current branch not found, unexpected output from `git branch`"
        | head::tail ->
            if (head.StartsWith("*")) then
                let branchName = head.Substring("* ".Length)
                branchName
            else
                GetBranchFromGitBranch(tail)

    let gitWhich = Process.Execute({ Command = "which"; Arguments = "git" }, Echo.Off)
    if (gitWhich.ExitCode <> 0) then
        String.Empty
    else
        let gitLog = Process.Execute({ Command = "git"; Arguments = "log --oneline" }, Echo.Off)
        if (gitLog.ExitCode <> 0) then
            String.Empty
        else
            let gitBranch = Process.Execute({ Command = "git"; Arguments = "branch" }, Echo.Off)
            if (gitBranch.ExitCode <> 0) then
                failwith "Unexpected git behaviour, as `git log` succeeded but `git branch` didn't"
            else
                let branchesOutput = gitBranch.Output.StdOut.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries) |> List.ofSeq
                let branch = GetBranchFromGitBranch(branchesOutput)
                let gitLogCmd = { Command = "git"; Arguments = "log --no-color --first-parent -n1 --pretty=format:%h" }
                let gitLastCommit = Process.Execute(gitLogCmd, Echo.Off)
                if (gitLastCommit.ExitCode <> 0) then
                    failwith "Unexpected git behaviour, as `git log` succeeded before but not now"

                let lines = gitLastCommit.Output.StdOut.Split([|Environment.NewLine|], StringSplitOptions.RemoveEmptyEntries)
                if (lines.Length <> 1) then
                    failwith "Unexpected git output for special git log command"
                else
                    let lastCommitSingleOutput = lines.[0]
                    sprintf "(%s/%s)" branch lastCommitSingleOutput

let repoInfo = GetRepoInfo()

Console.WriteLine()
Console.WriteLine(sprintf
                      "\tConfiguration summary for fsx %s %s"
                      version repoInfo)
Console.WriteLine()
Console.WriteLine(sprintf
                      "\t* Installation prefix: %s"
                      prefix.FullName)
Console.WriteLine()

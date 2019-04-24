#!/usr/bin/env fsharpi

open System
open System.IO

#r "System.Configuration"
#load "InfraLib/Misc.fs"
#load "InfraLib/Process.fs"
#load "InfraLib/Git.fs"
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

let prefix = DirectoryInfo(Misc.GatherOrGetDefaultPrefix(Misc.FsxArguments(), false, None))

if not (prefix.Exists) then
    let warning = sprintf "WARNING: prefix doesn't exist: %s" prefix.FullName
    Console.Error.WriteLine (warning)

File.WriteAllText(Path.Combine(__SOURCE_DIRECTORY__, "build.config"),
                  sprintf "Prefix=%s" prefix.FullName)

let versionContents = File.ReadAllText(Path.Combine(__SOURCE_DIRECTORY__, "version.config"))
let version = versionContents.Substring("Version=".Length).Trim()

let repoInfo = Git.GetRepoInfo()

Console.WriteLine()
Console.WriteLine(sprintf
                      "\tConfiguration summary for fsx %s %s"
                      version repoInfo)
Console.WriteLine()
Console.WriteLine(sprintf
                      "\t* Installation prefix: %s"
                      prefix.FullName)
Console.WriteLine()

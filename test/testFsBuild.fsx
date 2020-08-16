#!/usr/bin/env fsx

open System
open System.IO
open System.Linq
open System.Text.RegularExpressions

#r "System.Configuration"
#load "../InfraLib/Misc.fs"
#load "../InfraLib/Process.fs"
open FSX.Infrastructure
open Process


let DEBUG = false

// todo before putting this on CI:
// - change below to SafeExecute

let nugetSubDirName = ".nuget"
let nugetSubDir = Path.Combine (Directory.GetCurrentDirectory(), nugetSubDirName) |> DirectoryInfo
nugetSubDir.Create()
let nugetExe = Path.Combine(nugetSubDir.FullName, "nuget.exe") |> FileInfo

Directory.SetCurrentDirectory __SOURCE_DIRECTORY__
let netStandardSampleFolder =
    Path.Combine(__SOURCE_DIRECTORY__, "netStandardSample")
    |> DirectoryInfo
let netStandardProjectName = "GWallet.Backend.NetStandard"
let netStandardProjectFolder =
    Path.Combine(netStandardSampleFolder.FullName, netStandardProjectName)
    |> DirectoryInfo
let netStandardProject =
    Path.Combine(netStandardProjectFolder.FullName, sprintf "%s.fsproj" netStandardProjectName)
    |> FileInfo
let sln =
    Path.Combine(netStandardSampleFolder.FullName, "netStandardSample.sln")
    |> FileInfo

let binFolder =
    Path.Combine(netStandardProjectFolder.FullName, "bin")
    |> DirectoryInfo
let objFolder =
    Path.Combine(netStandardProjectFolder.FullName, "obj")
    |> DirectoryInfo

if binFolder.Exists then
    binFolder.Delete true
if objFolder.Exists then
    objFolder.Delete true

if not nugetExe.Exists then
    Directory.SetCurrentDirectory nugetSubDir.FullName
    Process.Execute({ Command = "wget"; Arguments = "https://dist.nuget.org/win-x86-commandline/v5.4.0/nuget.exe" },
                    Echo.All)
    |> ignore

// strangely enough, using stockmono from 20.04 on a project fails (but solution works...)
let nugetSlnProc = Process.Execute({ Command = nugetExe.FullName; Arguments = sprintf "restore %s" sln.FullName },
                                   Echo.Off)

// old versions of Mono don't work with nuget 5.4.0 (and even if we downloaded a 4.5.1 version, nuget team have disabled
// old TLS versions in their server (and old version of Mono don't support newer versions); so just skip it
if nugetSlnProc.ExitCode <> 0 then
    Console.Error.WriteLine "nuget execution failed; this is likely because of using an old version of Mono; skipping..."
    Environment.Exit 0

let netStandardBinFolder =
    Path.Combine(binFolder.FullName, "Debug", "netstandard2.0")
    |> DirectoryInfo

let msbuild = "msbuild"
if not (Process.CommandWorksInShell msbuild) then
    Console.Error.WriteLine "no msbuild found; skipping..."
    Environment.Exit 0

let nugetProjProc = Process.SafeExecute({ Command = nugetExe.FullName
                                          Arguments = sprintf "restore %s" netStandardProject.FullName },
                                        Echo.Off)

let msbuildProc = Process.Execute({ Command = msbuild; Arguments = netStandardProject.FullName }, Echo.All)
netStandardBinFolder.Refresh()
if (not netStandardBinFolder.Exists) ||
   (not (netStandardBinFolder.EnumerateFiles().Any(fun file -> file.FullName.EndsWith(sprintf "%s.dll" netStandardProjectName)))) then
    Console.Error.WriteLine "msbuild compilation didn't work? (test setup)"
    Environment.Exit 1

let fscExeIndex = msbuildProc.Output.StdOut.IndexOf "fsc.exe"
if fscExeIndex < 0 then
    Console.Error.WriteLine "msbuild compilation didn't output fsc.exe? (test setup, clean didn't work?)"
    Environment.Exit 1

let fromFscExe =
    let removeTrailingWhiteSpace (str: string) =
        // https://stackoverflow.com/a/2865931/544947
        Regex.Replace(str, @"^\s*$\n", "\n", RegexOptions.Multiline)
    msbuildProc.Output.StdOut.Substring fscExeIndex |> removeTrailingWhiteSpace
let endOfFscCall = Environment.NewLine + Environment.NewLine
let trimEnd = fromFscExe.IndexOf endOfFscCall
let trimEndPos = trimEnd + endOfFscCall.Length
let fscCall = fromFscExe.Substring(0, trimEndPos)

if binFolder.Exists then
    binFolder.Delete true
if objFolder.Exists then
    objFolder.Delete true

let ParseArgsCall(call: string): List<string*Option<string>> =
    let rec parseChunks (chunks: seq<string>) =
        match Seq.tryHead chunks with
        | None -> List.Empty
        | Some fstChunk ->
            let trimmedChunk = fstChunk.Trim()
            let colonIndex = trimmedChunk.IndexOf ":"
            let value =
                if colonIndex < 0 then
                    None
                else
                    trimmedChunk.Substring(colonIndex + 1) |> Some
            let arg =
                if colonIndex < 0 then
                    trimmedChunk
                else
                    trimmedChunk.Substring(0, colonIndex)
            (arg,value)::(parseChunks (Seq.tail chunks))
    let sanitizedCall = call.Trim()
    let args = sanitizedCall.Split([|' '|], StringSplitOptions.RemoveEmptyEntries)
                            .Skip 1
    parseChunks args

let CompareSeqs (foo: seq<'T>) (bar: seq<'T>) (comparePredicate: Option<'T*'T->bool>) =
    let fooList = foo |> List.ofSeq |> List.sort
    let barList = bar |> List.ofSeq |> List.sort
    if not (barList.SequenceEqual fooList) then
        for i in 0 .. (Math.Min(fooList.Length, barList.Length) - 1) do
            let fooItem,barItem = fooList.Item i,barList.Item i
            match comparePredicate with
            | None ->
                if fooItem <> barItem then
                    failwithf "Element %i differs: %A vs %A" i fooItem barItem
            | Some pred ->
                if not (pred(fooItem, barItem)) && not (pred(fooItem, barItem)) then
                    failwithf "Element %i differs: %A vs %A" i fooItem barItem
    if barList.Length <> fooList.Length then
        Console.Error.WriteLine (sprintf "Sequnces have different number of elements: %i vs %i"
                                         barList.Length fooList.Length)
        for i in Math.Min(fooList.Length, barList.Length) .. (Math.Max(fooList.Length, barList.Length) - 1) do
            let bigger =
                if barList.Length > fooList.Length then
                    barList
                else
                    fooList
            Console.Error.WriteLine(sprintf "Element missing: %A" (bigger.Item i))
        failwithf "Sequnces have different number of elements: %i vs %i" barList.Length fooList.Length

let CompareArgs (argsMsBuild: List<string*Option<string>>) (argsFsBuild: List<string*Option<string>>) =
    let flagValueArgs args =
        List.filter (fun (k: string, v: Option<string>) -> v.IsSome) args
    let flagValueArgsMsBuild, flagValueArgsFsBuild = flagValueArgs argsMsBuild, flagValueArgs argsFsBuild
    Console.WriteLine "About to compare flag value args..."
    CompareSeqs flagValueArgsMsBuild flagValueArgsFsBuild None
    Console.WriteLine "Done."

    let fsFiles args =
        List.filter (fun (k: string, v: Option<string>) -> k.EndsWith ".fs") args
    let fsFilesMsBuild, fsFilesFsBuild = fsFiles argsMsBuild, fsFiles argsFsBuild
    Console.WriteLine "About to compare .fs args..."
    CompareSeqs fsFilesMsBuild fsFilesFsBuild
                // the below comparison predicate is because we might use full path while msbuild uses a relative one
                (Some (fun ((k1,v1),(k2,v2)) -> k2.EndsWith k1))
    Console.WriteLine "Done."

    let singleArgs args =
        List.filter (fun (k: string, v: Option<string>) -> v.IsNone && not (k.EndsWith ".fs")) args
    let singleArgsMsBuild, singleArgsFsBuild = singleArgs argsMsBuild, singleArgs argsFsBuild
    Console.WriteLine "About to compare all single args (except .fs files)..."
    CompareSeqs singleArgsMsBuild singleArgsFsBuild None
    Console.WriteLine "Done."

let fscArgs = ParseArgsCall fscCall

let fsBuildTool = Path.Combine(__SOURCE_DIRECTORY__, "..", "Tools", "fsBuild.fsx") |> FileInfo
let fsBuildProc = Process.SafeExecute({ Command = fsBuildTool.FullName;
                                        Arguments = netStandardProject.FullName },
                                      Echo.OutputOnly)

let fsBuildFscCall = fsBuildProc.Output.StdOut
let fsBuildArgs = ParseArgsCall fsBuildFscCall

if DEBUG then
    let AnalyzeArgs(args: List<string*Option<string>>) =
        Console.WriteLine(sprintf "There are %i args:" args.Length)
        let singleArgs =
            List.filter (fun (k: string, v: Option<string>) -> v.IsNone) args
        Console.WriteLine(sprintf "- %i of them are single args" singleArgs.Length)
        let fsFiles =
            List.filter (fun (k: string, v: Option<string>) -> k.EndsWith ".fs") singleArgs
        Console.WriteLine(sprintf "- (and %i are .fs files)" fsFiles.Length)
        let valueArgs =
            List.filter (fun (k: string, v: Option<string>) -> v.IsSome) args
        Console.WriteLine(sprintf "- %i of them are flags with a value" valueArgs.Length)

    AnalyzeArgs fscArgs
    AnalyzeArgs fsBuildArgs
    Console.WriteLine "_______________________________________________________________________"

CompareArgs fscArgs fsBuildArgs

Console.WriteLine "Success"
Environment.Exit 0


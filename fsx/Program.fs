// this script is the equivalent of unixy launcher.sh script but for windows (where we're sure a FSI exists)

open System
open System.IO
open System.Text
open System.Linq
open System.Diagnostics

open Fsdk
open Fsdk.Misc
open Fsdk.Process
open FSX.Compiler

type FsxScriptDiscoveryInfo =
    | FsxFsxNotFoundYet
    | FsxFsxFoundButNoFsxScriptFoundYet
    | FsxFsxFoundAndFsxScriptNameSupplied of _userScriptName: string

let assemblyExecutableExtension =
#if !LEGACY_FRAMEWORK
    "dll"
#else
    "exe"
#endif


let SplitArgsIntoFsxcArgsAndUserArgs() : seq<string> * string * seq<string> =
    let rec userArgsInternal
        (fsxScriptDiscoverySoFar: FsxScriptDiscoveryInfo)
        (fsxcArgsSoFar: List<string>)
        (userArgsSoFar: List<string>)
        (nextArgs: List<string>)
        : seq<string> * string * seq<string> =
        match nextArgs, fsxScriptDiscoverySoFar with
        | [], FsxFsxFoundAndFsxScriptNameSupplied userScriptName ->
            let finalFscxArgs = fsxcArgsSoFar |> List.rev |> Seq.ofList
            let finalUserArgs = userArgsSoFar |> List.rev |> Seq.ofList
            finalFscxArgs, userScriptName, finalUserArgs
        | [], _ ->
            failwith(sprintf "fsx.%s not found" assemblyExecutableExtension)
        | head :: tail, fsxScriptDiscoverySoFar ->
            match fsxScriptDiscoverySoFar, head with
            | FsxFsxNotFoundYet, arg when
                arg.Split(Path.DirectorySeparatorChar).Last()
                    .EndsWith(sprintf "fsx.%s" assemblyExecutableExtension)
                ->
                if not fsxcArgsSoFar.IsEmpty then
                    failwith
                        "no fsxc args should have been added yet if FsxFsxNotFoundYet"

                if not userArgsSoFar.IsEmpty then
                    failwith
                        "no fsxc args should have been added yet if FsxFsxNotFoundYet"

                userArgsInternal
                    FsxFsxFoundButNoFsxScriptFoundYet
                    List.Empty
                    List.Empty
                    tail
            | FsxFsxNotFoundYet, _likelyFsiExePath ->
                if not fsxcArgsSoFar.IsEmpty then
                    failwith
                        "no fsxc args should have been added yet if FsxFsxNotFoundYet"

                if not userArgsSoFar.IsEmpty then
                    failwith
                        "no fsxc args should have been added yet if FsxFsxNotFoundYet"

                userArgsInternal FsxFsxNotFoundYet List.empty List.Empty tail
            | FsxFsxFoundButNoFsxScriptFoundYet, arg when
                arg
                    .Split(Path.DirectorySeparatorChar)
                    .Last()
                    .EndsWith ".fsx"
                ->
                if not userArgsSoFar.IsEmpty then
                    failwith
                        "no fsxc args should have been added yet if FsxFsxNotFoundYet"

                userArgsInternal
                    (FsxFsxFoundAndFsxScriptNameSupplied arg)
                    fsxcArgsSoFar
                    List.empty
                    tail
            | FsxFsxFoundButNoFsxScriptFoundYet, fsxcArg ->
                if not userArgsSoFar.IsEmpty then
                    failwith
                        "no fsxc args should have been added yet if FsxFsxFoundButNoFsxScriptFoundYet"

                userArgsInternal
                    FsxFsxFoundButNoFsxScriptFoundYet
                    (fsxcArg :: fsxcArgsSoFar)
                    List.empty
                    tail
            | (FsxFsxFoundAndFsxScriptNameSupplied userScriptName), userArg ->
                userArgsInternal
                    (FsxFsxFoundAndFsxScriptNameSupplied userScriptName)
                    fsxcArgsSoFar
                    (userArg :: userArgsSoFar)
                    tail


    Environment.GetCommandLineArgs()
    |> List.ofArray
    |> userArgsInternal FsxFsxNotFoundYet List.empty List.empty

let InjectBinSubfolderInPath(userScript: FileInfo) =
    if not(userScript.FullName.EndsWith ".fsx") then
        failwithf
            "Assertion failed: %s should end with .fsx"
            userScript.FullName

    let binPath =
        match userScript.FullName.LastIndexOf Path.DirectorySeparatorChar with
        | index when index >= 0 ->
            let path = userScript.FullName.Substring(0, index)

            sprintf
                "%s%sbin%s%s.exe"
                path
                (Path.DirectorySeparatorChar.ToString())
                (Path.DirectorySeparatorChar.ToString())
                (Path.GetFileName userScript.FullName)
        | _ ->
            sprintf
                "bin%s%s.exe"
                (Path.DirectorySeparatorChar.ToString())
                (Path.GetFileName userScript.FullName)

    FileInfo binPath

let fsxcArgs, userScript, userArgs = SplitArgsIntoFsxcArgsAndUserArgs()

let userScriptFile = FileInfo userScript

let fsxcMainArguments =
    Seq.append fsxcArgs (Seq.singleton userScript) |> Seq.toArray

Program.Main fsxcMainArguments |> ignore

let finalLaunch =
    {
        Command = (InjectBinSubfolderInPath userScriptFile).FullName
        Arguments = String.Join(" ", userArgs)
    }

let finalProc = Process.Execute(finalLaunch, Echo.OutputOnly)
// FIXME: maybe using a .fsx file as a launcher in Windows wasn't the best idea after all, because it means
// that, on Windows, fsx will run fsharpi while the compiled user script is running, which means that the
// memory gains of using fsx instead of fsharpi (as explained in the ReadMe.md file) don't exist for this OS
// (while in Unix, i.e. Linux and macOS, they exist because we use a bash script which uses 'exec')
match finalProc.Result with
| Error(exitCode, _errOutput) -> Environment.Exit exitCode
| _ -> Environment.Exit 0

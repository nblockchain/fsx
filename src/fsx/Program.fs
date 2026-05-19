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


let SplitArgsIntoFsxcArgsAndUserArgs
    ()
    : seq<string> * Option<string> * seq<string> =
    let rec userArgsInternal
        (fsxScriptDiscoverySoFar: FsxScriptDiscoveryInfo)
        (fsxcArgsSoFar: List<string>)
        (userArgsSoFar: List<string>)
        (nextArgs: List<string>)
        : seq<string> * Option<string> * seq<string> =
        match nextArgs, fsxScriptDiscoverySoFar with
        | [], FsxFsxFoundAndFsxScriptNameSupplied userScriptName ->
            let finalFscxArgs = fsxcArgsSoFar |> List.rev |> Seq.ofList
            let finalUserArgs = userArgsSoFar |> List.rev |> Seq.ofList
            finalFscxArgs, Some userScriptName, finalUserArgs
        | [], FsxFsxFoundButNoFsxScriptFoundYet ->
            let finalFscxArgs = fsxcArgsSoFar |> List.rev |> Seq.ofList
            let finalUserArgs = userArgsSoFar |> List.rev |> Seq.ofList
            finalFscxArgs, None, finalUserArgs
        | [], FsxFsxNotFoundYet ->
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

let InjectBinSubfolderInPath(userScriptPath: string) =
    if not(userScriptPath.EndsWith ".fsx") then
        failwithf "Assertion failed: %s should end with .fsx" userScriptPath

    let binPath =
        match userScriptPath.LastIndexOf Path.DirectorySeparatorChar with
        | index when index >= 0 ->
            let path = userScriptPath.Substring(0, index)

            sprintf
                "%s%sbin%s%s.exe"
                path
                (Path.DirectorySeparatorChar.ToString())
                (Path.DirectorySeparatorChar.ToString())
                (Path.GetFileName userScriptPath)
        | _ ->
            sprintf
                "bin%s%s.exe"
                (Path.DirectorySeparatorChar.ToString())
                (Path.GetFileName userScriptPath)

    FileInfo binPath

let fsxcArgs, maybeUserScriptPath, userArgs = SplitArgsIntoFsxcArgsAndUserArgs()

let fsxcMainArguments =
    match maybeUserScriptPath with
    | Some userScriptPath ->
        Seq.append fsxcArgs (Seq.singleton userScriptPath) |> Seq.toArray
    | None -> fsxcArgs |> Seq.toArray

Program.OuterMain fsxcMainArguments |> ignore

match maybeUserScriptPath with
| None ->
    failwith(
        "Compilation of anything that is not an .fsx should have been rejected by fsx"
        + " and shouldn't have reached this point. Please report this bug."
    )
| _ -> ()

let finalLaunch =
    {
        Command =
            (InjectBinSubfolderInPath maybeUserScriptPath.Value)
                .FullName
        Arguments = String.Join(" ", userArgs)
    }

let finalProc = Process.Execute(finalLaunch, Echo.OutputOnly)
// FIXME: fsx being an F# project instead of a launcher script means that, on
// Windows (and in Unix when installed via 'dotnet tool install fsx'), fsx will be running the user script as
// child process, which may make the memory gains of using fsx instead of fsi/fsharpi
// (as explained in the ReadMe.md file) not as prominent (while in Unix, i.e. Linux and macOS, when the
// tool is not installed via `dotnet tool install fsx`, they still are what ReadMe.md claims because we use a
// bash script which uses 'exec') // TODO: measure measure!
match finalProc.Result with
| Error(exitCode, _errOutput) -> Environment.Exit exitCode
| _ -> Environment.Exit 0

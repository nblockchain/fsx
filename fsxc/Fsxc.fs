﻿namespace FSX.Compiler

open System
open System.IO
open System.Linq

open Fsdk
open Fsdk.Process

type Flag =
    | Force
    | OnlyCheck
    | Verbose
    | Debug

type ProvidedCommandLineArguments =
    {
        Flags: List<Flag>
        MaybeScript: Option<FileInfo>
    }

type ParsedCommandLineArguments =
    {
        Flags: List<Flag>
        Script: FileInfo
    }

type BinFolder =
    {
        Dir: DirectoryInfo
        Created: bool
    }

type ExeTarget =
    {
        Exe: FileInfo
        BinFolderCreated: bool
    }

type BuildResult =
    | Failure of BinFolder
    | Success of ExeTarget

type ProgramInvocationType =
    | FsxLauncherScript
    | FsxcPureInvocation

type ExtraExtension = string

exception NoScriptProvided

module Program =

#if LEGACY_FRAMEWORK
    let private nugetExeTmpLocation: Lazy<FileInfo> =
        lazy
            (let tmpDir = System.IO.Path.GetTempPath() |> DirectoryInfo

             let tmpNuget =
                 Path.Combine(tmpDir.FullName, "nuget.exe") |> FileInfo

             Network.DownloadNugetExe tmpNuget
             tmpNuget)
#endif

    let PrintUsage(invocationType: ProgramInvocationType) =
        let programInvocation, scriptOptions =
            match invocationType with
            | FsxcPureInvocation -> "fsxc", String.Empty
            | FsxLauncherScript -> "fsx", " [yourScriptOptions]"

        Console.WriteLine()

        let dotnetToolPrefix =
#if !LEGACY_FRAMEWORK
            "dotnet "
#else
            String.Empty
#endif

        Console.WriteLine(
            sprintf
                "Usage: %s%s [OPTION] yourScript.fsx%s"
                dotnetToolPrefix
                programInvocation
                scriptOptions
        )

        Console.WriteLine()
        Console.WriteLine "Options"

        Console.WriteLine "  -d, --debug     Show debugging information"

        Console.WriteLine
            "  -f, --force     Always generate binaries again even if existing binaries are new enough"

        Console.WriteLine "  -h, --help      Show this help"

        Console.WriteLine
            "  -k, --check     Only check if it compiles, removing generated binaries"

        Console.WriteLine
            "  -v, --verbose   Verbose mode, ideal for debugging purposes"

    let rec ParseArgsInternal
        (args: seq<string>)
        (finalArgs: ProvidedCommandLineArguments)
        : ProvidedCommandLineArguments =
        match Seq.tryHead args with
        | None -> finalArgs
        | Some arg ->
            let maybeFlag: Option<Flag> =
                if arg = "-f" || arg = "--force" then
                    Some Force
                elif arg = "-k" || arg = "--check" then
                    Some OnlyCheck
                elif arg = "-v" || arg = "--verbose" then
                    Some Verbose
                elif arg = "-d" || arg = "--debug" then
                    Some Debug
                elif arg.StartsWith "-" then
                    failwithf "Flag not recognized: %s" arg
                else
                    None

            let newArgs =
                match maybeFlag with
                | None ->
                    if not(arg.EndsWith ".fsx") then
                        failwithf
                            "Argument not recognized: %s. Only commands, or scripts ending with .fsx allowed"
                            arg
                    elif finalArgs.MaybeScript.IsSome then
                        failwith "Only one .fsx script allowed"
                    else
                        {
                            Flags = finalArgs.Flags
                            MaybeScript = Some(FileInfo arg)
                        }
                | Some flag ->
                    {
                        Flags = flag :: finalArgs.Flags
                        MaybeScript = finalArgs.MaybeScript
                    }

            ParseArgsInternal (Seq.tail args) newArgs

    let ParseArgs(args: seq<string>) : ParsedCommandLineArguments =
        let parsedArgs =
            ParseArgsInternal
                args
                {
                    Flags = []
                    MaybeScript = None
                }

        match parsedArgs.MaybeScript with
        | None -> raise NoScriptProvided
        | Some scriptFileName ->
            {
                Flags = parsedArgs.Flags
                Script = scriptFileName
            }

    let LOAD_PREPROCESSOR = "#load \""
    let REFNUGET_PREPROCESSOR = "#r \"nuget: "
    let REF_PREPROCESSOR = "#r \""

    type PreProcessorAction =
        | Skip
        | Load of string
        | Ref of string
        | NugetRef of name: string * version: Option<string>

    type PreProcessorConditional =
        | If
        | Else
        | EndIf

    type PreProcessorElement =
        | Action of PreProcessorAction
        | Conditional of PreProcessorConditional

    type LineKind =
        | Commented
        | Normal
        | PreProcessorAction of action: PreProcessorAction

    type LineAction =
        {
            Line: string
            LineKind: LineKind
        }

    type FsxScript =
        {
            Original: FileInfo
            Compilable: FileInfo
        }

    type CompilerInput =
        | SourceFile of FileInfo
        | Script of FsxScript
        | BclRef of string
        | CustomRef of string

    type ReadState =
        | NormalOperation
        | DeliverLinesUntilNextPreProcessorConditional
        | IgnoreLinesUntilNextPreProcessorConditional

    let GetBinFolderForAScript(script: FileInfo) =
        DirectoryInfo(Path.Combine(script.Directory.FullName, "bin"))

    let GetAutoGenerationTargets (orig: FileInfo) (extension: ExtraExtension) =
        let binDir = GetBinFolderForAScript(orig)

        let autogeneratedFileName =
            if (String.IsNullOrEmpty(extension)) then
                orig.Name
            else
                sprintf "%s.%s" orig.Name extension

        let autogeneratedFile =
            FileInfo(Path.Combine(binDir.FullName, autogeneratedFileName))

        autogeneratedFile

    let ReadScriptContents(origScript: FileInfo) : List<LineAction> =

        let readPreprocessorLine(line: string) : Option<PreProcessorElement> =
            let trimmedLine = line.TrimStart()

            if not(trimmedLine.StartsWith "#") then
                None
            elif (line.StartsWith "#!") then
                PreProcessorAction.Skip |> PreProcessorElement.Action |> Some
            elif (line.StartsWith LOAD_PREPROCESSOR) then
                let fileToLoad =
                    line.Substring(
                        LOAD_PREPROCESSOR.Length,
                        line.Length - LOAD_PREPROCESSOR.Length - 1
                    )

                PreProcessorAction.Load fileToLoad
                |> PreProcessorElement.Action
                |> Some
            elif line.StartsWith REFNUGET_PREPROCESSOR then
                let libToRef =
                    line.Substring(
                        REFNUGET_PREPROCESSOR.Length,

                        // to remove the last double-quote character
                        line.Length - REFNUGET_PREPROCESSOR.Length - 1
                    )


                let libName, maybeVersion =
                    if libToRef.Contains "," then
                        let versionPattern = ", Version="

                        if libToRef.Contains versionPattern then
                            let versionPatternIndex =
                                libToRef.IndexOf versionPattern

                            let theNameOnly =
                                libToRef.Substring(0, versionPatternIndex)

                            let versionNumber =
                                libToRef.Substring(
                                    versionPatternIndex + versionPattern.Length
                                )

                            theNameOnly, (Some versionNumber)
                        else
                            let commaIndex = libToRef.IndexOf ","
                            let theNameOnly = libToRef.Substring(0, commaIndex)

                            let versionNumber =
                                libToRef
                                    .Substring(commaIndex + ",".Length)
                                    .Trim()

                            theNameOnly, (Some versionNumber)

                    else
                        libToRef, None

                PreProcessorAction.NugetRef(libName, maybeVersion)
                |> PreProcessorElement.Action
                |> Some

            elif (line.StartsWith REF_PREPROCESSOR) then
                let libToRef =
                    line.Substring(
                        REF_PREPROCESSOR.Length,
                        line.Length - REF_PREPROCESSOR.Length - 1
                    )

                PreProcessorAction.Ref libToRef
                |> PreProcessorElement.Action
                |> Some
            elif trimmedLine.StartsWith "#else" then
                PreProcessorConditional.Else
                |> PreProcessorElement.Conditional
                |> Some
            elif trimmedLine.StartsWith "#endif" then
                PreProcessorConditional.EndIf
                |> PreProcessorElement.Conditional
                |> Some
            elif trimmedLine.StartsWith "#if" then
                PreProcessorConditional.If
                |> PreProcessorElement.Conditional
                |> Some
            else
                failwithf "Unrecognized preprocessor line: %s" line

        let rec readLines
            (lines: seq<string>)
            (currentReadState: ReadState)
            (acc: List<LineAction>)
            : List<LineAction> =

            match Seq.tryHead lines with
            | Some line ->
                let rest = Seq.tail lines

                let newAcc, newState =
                    let lineKind, newState =
                        match readPreprocessorLine line with
                        | Some(PreProcessorElement.Action action) ->
                            if currentReadState
                               <> IgnoreLinesUntilNextPreProcessorConditional then
                                LineKind.PreProcessorAction action,
                                currentReadState
                            else
                                LineKind.Commented, currentReadState
                        | Some(PreProcessorElement.Conditional cond) ->
                            match cond, currentReadState with
                            | PreProcessorConditional.Else,
                              IgnoreLinesUntilNextPreProcessorConditional ->
                                LineKind.Commented,
                                DeliverLinesUntilNextPreProcessorConditional
                            | PreProcessorConditional.Else,
                              DeliverLinesUntilNextPreProcessorConditional ->
                                LineKind.Commented,
                                IgnoreLinesUntilNextPreProcessorConditional
                            | PreProcessorConditional.EndIf,
                              IgnoreLinesUntilNextPreProcessorConditional ->
                                LineKind.Commented, NormalOperation
                            | PreProcessorConditional.EndIf,
                              DeliverLinesUntilNextPreProcessorConditional ->
                                LineKind.Commented, NormalOperation
                            | PreProcessorConditional.EndIf, NormalOperation ->
                                LineKind.Normal, NormalOperation
                            | PreProcessorConditional.If, _ ->
                                if not(line.Contains "LEGACY_FRAMEWORK") then
                                    match currentReadState with
                                    | IgnoreLinesUntilNextPreProcessorConditional ->
                                        LineKind.Commented, currentReadState
                                    | _ -> LineKind.Normal, currentReadState
                                else
                                    let newState =
                                        match line.Trim() with
                                        | "#if LEGACY_FRAMEWORK" ->
#if LEGACY_FRAMEWORK
                                            DeliverLinesUntilNextPreProcessorConditional
#else
                                            IgnoreLinesUntilNextPreProcessorConditional
#endif
                                        | "#if !LEGACY_FRAMEWORK" ->
#if LEGACY_FRAMEWORK
                                            IgnoreLinesUntilNextPreProcessorConditional
#else
                                            DeliverLinesUntilNextPreProcessorConditional
#endif

                                        | _ ->
                                            failwith
                                                "Only simple ifdef statements are supported for the LEGACY_FRAMEWORK define"

                                    LineKind.Commented, newState
                            | _, NormalOperation ->
                                LineKind.Normal, currentReadState

                        | None ->
                            match currentReadState with
                            | IgnoreLinesUntilNextPreProcessorConditional ->
                                LineKind.Commented, currentReadState
                            | _ -> LineKind.Normal, currentReadState

                    let newAcc =
                        {
                            Line = line
                            LineKind = lineKind
                        }
                        :: acc

                    newAcc, newState

                readLines rest newState newAcc
            | None -> acc

        if not origScript.Exists then
            raise
            <| FileNotFoundException(
                sprintf "Script not found?: %s" origScript.FullName,
                origScript.FullName
            )

        let contents = File.ReadAllText origScript.FullName

        let lines =
            contents.Split([| Environment.NewLine |], StringSplitOptions.None)

        let initialReadState = ReadState.NormalOperation
        let initialAcc = List.Empty
        readLines lines initialReadState initialAcc

    let GetParsedContentsAndOldestLastWriteTimeFromScriptOrItsDependencies
        (script: FileInfo)
        : List<LineAction> * DateTime =
        let scriptContents = ReadScriptContents script |> List.rev

        let lastWriteTimes =
            seq {
                yield script.LastWriteTime

                for maybeDep in scriptContents do
                    match maybeDep.LineKind with
                    | LineKind.PreProcessorAction preProcessorAction ->
                        match preProcessorAction with
                        | PreProcessorAction.Load file ->
                            let fileInfo =
                                FileInfo
                                <| Path.Combine(script.Directory.FullName, file)

                            if not fileInfo.Exists then
                                failwithf "Dependency %s not found" file
                            else
                                yield fileInfo.LastWriteTime
                        | PreProcessorAction.Ref ref ->
                            let fileInfo =
                                FileInfo
                                <| Path.Combine(script.Directory.FullName, ref)

                            if not fileInfo.Exists then
                                // must be a BCL lib (e.g. #r "System.Xml.Linq.dll")
                                ()
                            else
                                yield fileInfo.LastWriteTime
                        | _ -> ()
                    | _ -> ()
            }

        scriptContents, lastWriteTimes.Max()

    let BuildFsxScript
        (script: FileInfo)
        (contents: List<LineAction>)
        (verbose: bool)
        : BuildResult =
        if script = null then
            raise <| ArgumentNullException "script"

        let echo =
            if verbose then
                Echo.All
            else
                Echo.Off

        if not(script.FullName.EndsWith ".fsx") then
            invalidArg
                "script"
                "The script filename needs to end with .fsx extension"

        let binFolderExistedOriginally = GetBinFolderForAScript(script).Exists

        let preprocessScriptContents
            (origScript: FileInfo)
            (contents: List<LineAction>)
            : List<CompilerInput> * List<ExtraExtension> =

#if LEGACY_FRAMEWORK
            // from "Microsoft.Build", "16.11.0" to .../packages/Microsoft.Build.16.11.0/lib/net472/Microsoft.Build.dll
            let nugetRefToNormalRef
                (scriptFile: FileInfo)
                (pkgName: string)
                (version: Option<string>)
                : FileInfo =

                let allowedFrameworkProfilesDirs =
                    [
                        "net472"
                        "net471"
                        "net462"
                        "net461"
                        "net452"
                        "net45"
                        "netstandard2.0"
                    ]

                let binDir =
                    Path.Combine(scriptFile.Directory.FullName, "bin")
                    |> DirectoryInfo

                if not binDir.Exists then
                    binDir.Create()

                let nugetPkgsDir =
                    Path.Combine(binDir.FullName, "packages") |> DirectoryInfo

                if not nugetPkgsDir.Exists then
                    nugetPkgsDir.Create()

                let possibleLibDirs =
                    match version with
                    | None ->
                        Network.InstallNugetPackage
                            nugetExeTmpLocation.Value
                            nugetPkgsDir
                            pkgName
                            version
                            echo
                        |> ignore<ProcessResult>

                        seq {
                            // not sure if this orderBy is right...
                            for dir in
                                nugetPkgsDir
                                    .GetDirectories(sprintf "%s.*" pkgName)
                                    .OrderByDescending(fun dir -> dir.Name) do
                                yield
                                    Path.Combine(dir.FullName, "lib")
                                    |> DirectoryInfo
                        }

                    | Some version ->
                        Path.Combine(
                            nugetPkgsDir.FullName,
                            sprintf "%s.%s" pkgName version,
                            "lib"
                        )
                        |> DirectoryInfo
                        |> Seq.singleton

                let possibleLocations =
                    seq {
                        for libDir in possibleLibDirs do
                            for fxProfileDir in allowedFrameworkProfilesDirs do
                                let possibleFile =
                                    Path.Combine(
                                        libDir.FullName,
                                        fxProfileDir,
                                        sprintf "%s.dll" pkgName
                                    )
                                    |> FileInfo

                                if possibleFile.Exists then
                                    yield possibleFile
                    }

                let nugetLibFinalLocation = Seq.tryHead possibleLocations

                match nugetLibFinalLocation with
                | None ->

                    Network.InstallNugetPackage
                        nugetExeTmpLocation.Value
                        nugetPkgsDir
                        pkgName
                        version
                        echo
                    |> ignore<ProcessResult>

                    match Seq.tryHead possibleLocations with
                    | None ->
                        failwithf
                            "Nuget download finished but lib still not found inside for package %s"
                            pkgName
                    | Some location -> location
                | Some location -> location
#endif

            let initialFileContent =
                """[<AutoOpen>]
module FsxScriptInit
type FsiStub = { CommandLineArgs: array<string> }
let fsi = { CommandLineArgs = System.Environment.GetCommandLineArgs() }
"""

            let extraExtensionForMain: ExtraExtension = "fs"

            let autogeneratedFile =
                GetAutoGenerationTargets origScript extraExtensionForMain

            let binFolder = autogeneratedFile.Directory

            if not binFolder.Exists then
                Directory.CreateDirectory binFolder.FullName |> ignore

            File.Copy(origScript.FullName, autogeneratedFile.FullName, true)

            File.WriteAllText(
                autogeneratedFile.FullName,
                """module FsxScript
"""
            )

            let extraExtensionForInitFile: ExtraExtension = "init.fs"

            let initFile =
                GetAutoGenerationTargets origScript extraExtensionForInitFile

            File.WriteAllText(initFile.FullName, initialFileContent)

            let extraExtensions =
                [
                    extraExtensionForInitFile
                    extraExtensionForMain
                ]

            seq {
                // for !LEGACY_FRAMEWORK, this init file will be added to the fsproj file
#if LEGACY_FRAMEWORK
                yield CompilerInput.SourceFile initFile
#endif
                let startCommentInFSharp = "// "

                for maybeDep in contents do
                    let line = maybeDep.Line

                    match maybeDep.LineKind with
                    | LineKind.Commented ->
                        File.AppendAllText(
                            autogeneratedFile.FullName,
                            startCommentInFSharp + line + Environment.NewLine
                        )
                    | LineKind.Normal ->
                        // TODO: remove ".fs" from __SOURCE_FILE__ too, see https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/source-line-file-path-identifiers
                        let newLine =
                            line.Replace(
                                "__SOURCE_DIRECTORY__",
                                "(System.IO.Path.Combine(__SOURCE_DIRECTORY__, \"..\"))"
                            )

                        File.AppendAllText(
                            autogeneratedFile.FullName,
                            newLine + Environment.NewLine
                        )
                    | LineKind.PreProcessorAction action ->
                        File.AppendAllText(
                            autogeneratedFile.FullName,
                            startCommentInFSharp + line + Environment.NewLine
                        )

                        match action with
                        | PreProcessorAction.Skip -> ()
                        | PreProcessorAction.Load fileName ->
                            let file =
                                FileInfo(
                                    Path.Combine(
                                        origScript.Directory.FullName,
                                        fileName
                                    )
                                )

                            yield CompilerInput.SourceFile(file)

#if LEGACY_FRAMEWORK
                        | PreProcessorAction.NugetRef(nugetPkgName, version) ->
                            let downloadedRef =
                                nugetRefToNormalRef
                                    origScript
                                    nugetPkgName
                                    version

                            yield CompilerInput.CustomRef downloadedRef.FullName
#else

                        | PreProcessorAction.NugetRef(_nugetPkgName, _version) ->
                            ()
#endif
                        | PreProcessorAction.Ref refName ->
                            let maybeFile =
                                FileInfo(
                                    Path.Combine(
                                        origScript.Directory.FullName,
                                        refName
                                    )
                                )

                            if maybeFile.Exists then
                                yield CompilerInput.CustomRef maybeFile.FullName
                            else
                                // must be a BCL lib (e.g. #r "System.Xml.Linq.dll")
                                yield CompilerInput.BclRef refName

                yield
                    CompilerInput.Script(
                        {
                            Original = origScript
                            Compilable = autogeneratedFile
                        }
                    )
            }
            |> List.ofSeq,
            extraExtensions

#if LEGACY_FRAMEWORK
        let getSourceFiles(flags: seq<CompilerInput>) : seq<FileInfo> =
            seq {
                for f in flags do
                    match f with
                    | CompilerInput.SourceFile file -> yield file
                    | CompilerInput.Script script -> yield script.Compilable
                    | _ -> ()
            }

        let getCompilerReferences(flags: seq<CompilerInput>) : seq<string> =
            seq {
                for flag in flags do
                    match flag with
                    | CompilerInput.BclRef refName
                    | CompilerInput.CustomRef refName ->
                        yield sprintf "--reference:%s" refName
                    | _ -> ()
            }
#else
        let generateProjectFile
            (origScript: FileInfo)
            (contents: List<LineAction>)
            (extraExtensions: seq<ExtraExtension>)
            : FileInfo =
            let projectFile = GetAutoGenerationTargets origScript "fsproj"

            let addFile relativeOrAbsoluteFilePath =
                let fsProjFragment =
                    sprintf
                        "<ItemGroup><Compile Include=\"%s\" /></ItemGroup>"
                        relativeOrAbsoluteFilePath

                File.AppendAllText(
                    projectFile.FullName,
                    fsProjFragment + Environment.NewLine
                )

            let rec iterate(lines: List<LineAction>) : unit =
                match lines with
                | head :: tail ->
                    match head.LineKind with
                    | LineKind.PreProcessorAction action ->
                        match action with
                        | PreProcessorAction.NugetRef
                            (
                                nugetPkgName, maybeVersion
                            ) ->
                            let fsprojFragment =
                                sprintf
                                    "<ItemGroup><PackageReference Include=\"%s\" "
                                    nugetPkgName

                            let fsprojFragmentEnd =
                                match maybeVersion with
                                | None ->
                                    "Version=\"*\"><IncludeAssets>all</IncludeAssets></PackageReference></ItemGroup>"
                                | Some version ->
                                    sprintf
                                        "Version=\"%s\"><IncludeAssets>all</IncludeAssets></PackageReference></ItemGroup>"
                                        version

                            File.AppendAllText(
                                projectFile.FullName,
                                fsprojFragment
                                + fsprojFragmentEnd
                                + Environment.NewLine
                            )
                        | PreProcessorAction.Load fileName ->
                            addFile(
                                sprintf
                                    "..%c%s"
                                    Path.DirectorySeparatorChar
                                    fileName
                            )

                        | PreProcessorAction.Ref refName ->
                            let fsProjFragment =
                                if refName.ToLower().EndsWith(".dll") then
                                    sprintf
                                        "<ItemGroup><Reference Include=\"%s\"><HintPath>..%c%s</HintPath></Reference></ItemGroup>"
                                        (FileInfo refName).Name
                                        Path.DirectorySeparatorChar
                                        refName
                                else // bcl ref
                                    sprintf
                                        "<ItemGroup><Reference Include=\"%s\"/></ItemGroup>"
                                        refName

                            File.AppendAllText(
                                projectFile.FullName,
                                fsProjFragment + Environment.NewLine
                            )
                        | _ -> ()
                    | _ -> ()

                    iterate tail
                | [] -> ()

            let initialProjectContents =
                """<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net6.0</TargetFramework>

    <!-- the two settings below allow the binaries sit directly in subfolder /bin/ -->
    <OutputPath>.</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  </PropertyGroup>
"""

            let initialProjectContents = initialProjectContents

            File.WriteAllText(projectFile.FullName, initialProjectContents)

            iterate contents

            for ext in extraExtensions do
                addFile(sprintf "%s.%s" origScript.Name ext)

            File.AppendAllText(projectFile.FullName, "</Project>")

            projectFile
#endif

        if verbose then
            Console.WriteLine(sprintf "Building %s" script.FullName)

        let binFolder = GetBinFolderForAScript script

        let compilerInputs, extraExtensions =
            preprocessScriptContents script contents
#if !LEGACY_FRAMEWORK
        let projectFile = generateProjectFile script contents extraExtensions
#else // dummy for to just prevent "unused" warning, meh
        for _ext in extraExtensions do
            ()
#endif

        let exitCode, exeTarget =
            let exeTarget = GetAutoGenerationTargets script "exe"
#if LEGACY_FRAMEWORK
            let filesToCompile = getSourceFiles compilerInputs
            let sourceFiles = String.Join(" ", filesToCompile)

            let refs = String.Join(" ", getCompilerReferences compilerInputs)
#endif

            let buildConfigIsDebug =
#if DEBUG
                true
#else
                false
#endif

#if !LEGACY_FRAMEWORK
            let buildConfig =
                if buildConfigIsDebug then
                    "Debug"
                else
                    "Release"
#endif

            let fscompilerflags =


#if !LEGACY_FRAMEWORK
                sprintf
                    "build %s --configuration %s"
                    projectFile.FullName
                    buildConfig
#else
                let maybeDebugDefine =
                    if buildConfigIsDebug then
                        "--define:DEBUG"
                    else
                        String.Empty

                (sprintf
                    "%s %s %s --warnaserror --target:exe --out:%s %s"
                    refs
                    "--define:LEGACY_FRAMEWORK"
                    maybeDebugDefine
                    exeTarget.FullName
                    sourceFiles)
#endif

            let fsharpCompilerCommand =
#if !LEGACY_FRAMEWORK
                "dotnet"
#else
                match Misc.GuessPlatform() with
                | Misc.Platform.Windows ->
                    match Process.VsWhere "**\\fsc.exe" with
                    | None -> failwith "fsc.exe not found"
                    | Some fscExe -> fscExe
                | _ -> "fsharpc"
#endif

            let proc =
                Process.Execute(
                    {
                        Command = fsharpCompilerCommand
                        Arguments = fscompilerflags
                    },
                    echo
                )

            let exitCode =
                match proc.Result with
                | Error(exitCode, output) ->
                    output.PrintToConsole()
                    exitCode
                | ProcessResultState.Success _
                | WarningsOrAmbiguous _ -> 0

            exitCode, exeTarget

        let success =
            match exitCode with
            | 0 ->
                for compilerInput in compilerInputs do
                    match compilerInput with
                    | CompilerInput.CustomRef refFullPath ->
                        File.Copy(
                            refFullPath,
                            Path.Combine(
                                binFolder.FullName,
                                Path.GetFileName refFullPath
                            ),
                            true
                        )
                    | _ -> ()

                true
            | _ -> false

        if not success then
            Console.Error.WriteLine "Build failure"

            BuildResult.Failure(
                {
                    Dir = binFolder
                    Created = (not binFolderExistedOriginally)
                }
            )
        else
            BuildResult.Success(
                {
                    Exe = exeTarget
                    BinFolderCreated = (not binFolderExistedOriginally)
                }
            )

    let GetAlreadyBuiltExecutable
        (exeTarget: FileInfo)
        (lastWriteTimeOfSourceFile: DateTime)
        : Option<FileInfo> =
        let binFolder = exeTarget.Directory

        if not binFolder.Exists then
            None
        elif binFolder.LastWriteTime < lastWriteTimeOfSourceFile then
            None
        elif not exeTarget.Exists then
            None
        elif exeTarget.LastWriteTime < lastWriteTimeOfSourceFile then
            None
        else
            Some exeTarget

    let Build
        parsedArgs
        (generateArtifacts: bool)
        (contents: List<LineAction>)
        (verbose: bool)
        =
        let buildResult = BuildFsxScript parsedArgs.Script contents verbose

        match buildResult with
        | Failure binFolder ->
            if binFolder.Created then
                binFolder.Dir.Delete true

            Environment.Exit 1
            failwith "Unreachable"

        | Success exeTarget ->
            if not generateArtifacts then
                if exeTarget.BinFolderCreated then
                    exeTarget.Exe.Directory.Delete true

            exeTarget.Exe

    let private InnerMain
        (invocationType: ProgramInvocationType)
        (argv: array<string>)
        =
        if argv.Length = 0 then
            Console.Error.WriteLine "Please pass the .fsx script as an argument"
            PrintUsage invocationType
            Environment.Exit 1

        if argv.Length = 1 && argv.[0] = "--help" then
            PrintUsage invocationType
            Environment.Exit 0

        let parsedArgs =
            try
                ParseArgs argv
            with
            | :? NoScriptProvided ->
                Console.Error.WriteLine
                    "At least one .fsx script is required as input. Use --help for info."

                Environment.Exit 1
                failwith "Unreachable"

        let check, force =
            parsedArgs.Flags.Contains Flag.OnlyCheck,
            parsedArgs.Flags.Contains Flag.Force

        let verbose = parsedArgs.Flags.Contains Flag.Verbose

        let debug = parsedArgs.Flags.Contains Flag.Debug

        if debug then
            let cmdLineArgs = Environment.GetCommandLineArgs()

            Console.WriteLine(
                sprintf "DEBUG: __SOURCE_FILE__: %s" __SOURCE_FILE__
            )

            Console.WriteLine(
                sprintf
                    "DEBUG: Env.CmdLineArgs: %s"
                    (String.Join(",", cmdLineArgs))
            )

        let scriptContents, lastWriteTimeOfSourceFiles =
            GetParsedContentsAndOldestLastWriteTimeFromScriptOrItsDependencies
                parsedArgs.Script

        if check || force then
            let generateArtifacts = not check
            Build parsedArgs generateArtifacts scriptContents verbose |> ignore
            Environment.Exit 0

        let exeTarget = GetAutoGenerationTargets parsedArgs.Script "exe"

        let maybeExe =
            GetAlreadyBuiltExecutable exeTarget lastWriteTimeOfSourceFiles

        if maybeExe.IsNone then
            Build parsedArgs true scriptContents verbose |> ignore
        elif verbose then
            Console.WriteLine "Up-to-date binary found, skipping compilation"

        0 // return an integer exit code

    let private WrapperForMain invocationType argv =
        try
            InnerMain invocationType argv
        finally
#if LEGACY_FRAMEWORK
            if nugetExeTmpLocation.IsValueCreated then
                nugetExeTmpLocation.Value.Delete()
#else
            ()
#endif

    let internal Main argv =
        WrapperForMain ProgramInvocationType.FsxcPureInvocation argv

    let OuterMain argv =
        WrapperForMain ProgramInvocationType.FsxLauncherScript argv

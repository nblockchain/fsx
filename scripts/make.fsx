open System
open System.IO
open System.Net
open System.Linq
open System.Diagnostics

#r "System.Configuration"
open System.Configuration

#load "../InfraLib/Misc.fs"
#load "../InfraLib/Process.fs"
#load "../InfraLib/Network.fs"
#load "../InfraLib/Git.fs"

open FSX.Infrastructure
open Process

let ScriptsDir = __SOURCE_DIRECTORY__ |> DirectoryInfo
let RootDir = Path.Combine(ScriptsDir.FullName, "..") |> DirectoryInfo
let TestDir = Path.Combine(RootDir.FullName, "test") |> DirectoryInfo
let ToolsDir = Path.Combine(RootDir.FullName, "Tools") |> DirectoryInfo
let InfraLibDir = Path.Combine(RootDir.FullName, "InfraLib") |> DirectoryInfo
let NugetDir = Path.Combine(RootDir.FullName, ".nuget") |> DirectoryInfo
let NugetExe = Path.Combine(NugetDir.FullName, "nuget.exe") |> FileInfo

type BinaryConfig =
    | Debug
    | Release

    override self.ToString() =
        sprintf "%A" self

let GatherTarget(args: List<string>) : Option<string> =
    let rec gatherTarget
        (args: List<string>)
        (targetSet: Option<string>)
        : Option<string> =
        match args with
        | [] -> targetSet
        | head :: tail ->
            if targetSet.IsSome then
                failwith "only one target can be passed to make"

            gatherTarget tail (Some head)

    gatherTarget args None

let mainBinariesDir binaryConfig =
#if !LEGACY_FRAMEWORK
    Path.Combine(
        RootDir.FullName,
        "fsxc",
        "bin",
        binaryConfig.ToString(),
        "net6.0"
    )
#else
    Path.Combine(RootDir.FullName, "fsxc", "bin", binaryConfig.ToString())
#endif
    |> DirectoryInfo

let PrintNugetVersion() =
    if not NugetExe.Exists then
        false
    else
        let nugetProc =
            Network.RunNugetCommand NugetExe String.Empty Echo.OutputOnly false

        match nugetProc.Result with
        | ProcessResultState.Success _ -> true
        | ProcessResultState.WarningsOrAmbiguous _output ->
            Console.WriteLine()
            Console.Out.Flush()

            failwith
                "nuget process succeeded but the output contained warnings ^"
        | ProcessResultState.Error(_exitCode, _output) ->
            Console.WriteLine()
            Console.Out.Flush()
            failwith "nuget process' output contained errors ^"

let ConfigCommandCheck
    (commandNamesByOrderOfPreference: seq<string>)
    (exitIfNotFound: bool)
    : Option<string> =
    let rec configCommandCheck currentCommandNamesQueue allCommands =
        match Seq.tryHead currentCommandNamesQueue with
        | Some currentCommand ->
            //Console.Write (sprintf "checking for %s... " currentCommand)
            if not(Process.CommandWorksInShell currentCommand) then
                //Console.WriteLine "not found"
                configCommandCheck
                    (Seq.tail currentCommandNamesQueue)
                    allCommands
            else
                //Console.WriteLine "found"
                currentCommand |> Some
        | None ->
            Console.Error.WriteLine(
                sprintf
                    "Error, please install %s"
                    (String.Join(" or ", List.ofSeq allCommands))
            )

            if exitIfNotFound then
                Environment.Exit 1
                failwith "unreachable"
            else
                None

    configCommandCheck
        commandNamesByOrderOfPreference
        commandNamesByOrderOfPreference

let FindBuildTool() : string * string =
    match Misc.GuessPlatform() with
    | Misc.Platform.Linux
    | Misc.Platform.Mac ->
        failwith
            "cannot find buildTool because this script is not ready for Unix yet"
    | Misc.Platform.Windows ->
#if !LEGACY_FRAMEWORK
        "dotnet", "build"
#else
        //we need to call "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -find MSBuild\**\Bin\MSBuild.exe
        let programFiles =
            Environment.GetFolderPath Environment.SpecialFolder.ProgramFilesX86

        let vswhereExe =
            Path.Combine(
                programFiles,
                "Microsoft Visual Studio",
                "Installer",
                "vswhere.exe"
            )
            |> FileInfo

        ConfigCommandCheck(List.singleton vswhereExe.FullName) |> ignore

        let vswhereCmd =
            {
                Command = vswhereExe.FullName
                Arguments = "-find MSBuild\\**\\Bin\\MSBuild.exe"
            }

        let procResult = Process.Execute(vswhereCmd, Echo.Off)
        let msbuildPath = procResult.UnwrapDefault().Trim()
        msbuildPath, String.Empty
#endif

let BuildSolution
    (buildTool: string * string)
    (solutionFileName: string)
    (binaryConfig: BinaryConfig)
    (extraOptions: string)
    =
    let configOption = sprintf "/p:Configuration=%s" (binaryConfig.ToString())

    let buildToolExecutable, buildToolArg = buildTool

    let buildArgs =
        sprintf
            "%s %s %s %s"
            buildToolArg
            solutionFileName
            configOption
            extraOptions

    let buildProcess =
        Process.Execute(
            {
                Command = buildToolExecutable
                Arguments = buildArgs
            },
            Echo.All
        )

    match buildProcess.Result with
    | Error _ ->
        Console.WriteLine()
        Console.Out.Flush()

        Console.Error.WriteLine(
            sprintf
                "Build failed with build tool '%s %s' ^"
                buildToolExecutable
                buildToolArg
        )

        PrintNugetVersion() |> ignore
        Environment.Exit 1
    | _ -> ()

let JustBuild binaryConfig =
#if !LEGACY_FRAMEWORK
    let solFile = "fsx.sln"

    Process
        .Execute(
            {
                Command = "dotnet"
                Arguments = sprintf "restore %s" solFile
            },
            Echo.All
        )
        .UnwrapDefault()
    |> ignore<string>
#else
    let solFile = "fsx-legacy.sln"

    Network.RunNugetCommand
        NugetExe
        (sprintf "restore %s" solFile)
        Echo.All
        true
    |> ignore
#endif


    let buildTool = FindBuildTool()

    Console.WriteLine(
        sprintf "Building in %s mode..." (binaryConfig.ToString())
    )

    BuildSolution buildTool solFile binaryConfig String.Empty

let MakeAll() =
    let buildConfig = BinaryConfig.Debug
    JustBuild buildConfig
    buildConfig

let programFiles =
    Environment.GetEnvironmentVariable "ProgramW6432" |> DirectoryInfo

let fsxInstallationDir =
    Path.Combine(programFiles.FullName, "fsx") |> DirectoryInfo

#if !LEGACY_FRAMEWORK
let fsxBat = Path.Combine(ScriptsDir.FullName, "fsx.bat") |> FileInfo
#else
let fsxBat = Path.Combine(ScriptsDir.FullName, "fsx-legacy.bat") |> FileInfo
#endif

let fsxBatDestination =
    Path.Combine(fsxInstallationDir.FullName, "fsx.bat") |> FileInfo

let maybeTarget = GatherTarget(Misc.FsxOnlyArguments())

match maybeTarget with

| None
| Some "all" -> MakeAll() |> ignore

| Some "install" ->
    let buildConfig = BinaryConfig.Release
    JustBuild buildConfig

    if fsxInstallationDir.Exists then
        failwith "this script can't overwrite an existing installation yet" //TODO

    Console.WriteLine "Installing..."
    Console.WriteLine()

    Misc.CopyDirectoryRecursively(
        mainBinariesDir buildConfig,
        fsxInstallationDir,
        List.Empty
    )

    let fsiBat = Path.Combine(ToolsDir.FullName, "fsi.bat") |> FileInfo

    File.Copy(
        fsiBat.FullName,
        Path.Combine(fsxInstallationDir.FullName, fsiBat.Name)
    )

    let fsxLauncher = Path.Combine(RootDir.FullName, "launcher.fsx") |> FileInfo

    File.Copy(
        fsxLauncher.FullName,
        Path.Combine(fsxInstallationDir.FullName, "fsx.fsx")
    )

    File.Copy(fsxBat.FullName, fsxBatDestination.FullName)

    let infraLibInstallDir =
        Path.Combine(fsxInstallationDir.FullName, "InfraLib") |> DirectoryInfo

    if not infraLibInstallDir.Exists then
        Directory.CreateDirectory infraLibInstallDir.FullName
        |> ignore<DirectoryInfo>

    let miscFs = Path.Combine(InfraLibDir.FullName, "Misc.fs") |> FileInfo

    let miscFsTarget =
        Path.Combine(infraLibInstallDir.FullName, "Misc.fs") |> FileInfo

    File.Copy(miscFs.FullName, miscFsTarget.FullName)
    let processFs = Path.Combine(InfraLibDir.FullName, "Process.fs") |> FileInfo

    let processFsTarget =
        Path.Combine(infraLibInstallDir.FullName, "Process.fs") |> FileInfo

    File.Copy(processFs.FullName, processFsTarget.FullName)


    // FIXME: the below way of installing fsx into PATH env var seems to work, but somehow cannot be
    // tested inside CI, because `ConfigCommandCheck(List.singleton "fsx.bat")` fails, even though
    // Environment.GetEnvironmentVariable(pathEnvVarName, envVarScope) contains the new path (even
    // when testing this inside a different Makefile target -> "check")
    let pathEnvVarName = "PATH"
    let envVarScope = EnvironmentVariableTarget.Machine

    let currentPaths =
        Environment.GetEnvironmentVariable(pathEnvVarName, envVarScope)

    if not(currentPaths.Contains fsxInstallationDir.FullName) then
        let newPathEnvVar =
            sprintf
                "%s%c%s"
                fsxInstallationDir.FullName
                Path.PathSeparator
                currentPaths

        Environment.SetEnvironmentVariable(
            pathEnvVarName,
            newPathEnvVar,
            envVarScope
        )



    Console.WriteLine(
        sprintf "Successfully installed in %s" fsxInstallationDir.FullName
    )

| Some "check" ->

    // FIXME: contributor should be able to run 'make check' before 'make install'
    if not fsxBatDestination.Exists then
        Console.WriteLine "install first"
        Environment.Exit 1

    let testProcess =
        Process.Execute(
            {
                Command = fsxBatDestination.FullName
                Arguments = Path.Combine(ScriptsDir.FullName, "runTests.fsx")
            },
            Echo.All
        )

    match testProcess.Result with
    | Error _ -> failwith "Tests failed"
    | _ -> ()

    // the reason to write the result of this to a file is:
    // if error propagation is broken, then it would be broken as well for make.fsx
    // when trying to call runTests.fsx and wouldn't pick up an err
    let errorPropagationResultFile =
        Path.Combine(TestDir.FullName, "errProp.txt") |> FileInfo

    let errorPropagationResult =
        File
            .ReadAllText(errorPropagationResultFile.FullName)
            .Trim()

    match errorPropagationResult with
    | "1" -> failwith "Tests failed (error propagation)"
    | "0" -> ()
    | _ -> failwith "Unexpected output from tests (error propagation)"

| Some someOtherTarget ->
    Console.Error.WriteLine("Unrecognized target: " + someOtherTarget)
    Environment.Exit 1

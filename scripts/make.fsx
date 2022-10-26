open System
open System.IO
open System.Net
open System.Linq
open System.Diagnostics

#r "System.Configuration"
open System.Configuration

#load "../InfraLib/Misc.fs"
#load "../InfraLib/Process.fs"
#load "../InfraLib/Git.fs"

open FSX.Infrastructure
open Process

let ScriptsDir = __SOURCE_DIRECTORY__ |> DirectoryInfo
let RootDir = Path.Combine(ScriptsDir.FullName, "..") |> DirectoryInfo
let NugetDir = Path.Combine(RootDir.FullName, ".nuget") |> DirectoryInfo
let NugetExe = Path.Combine(NugetDir.FullName, "nuget.exe") |> FileInfo
let NugetUrl = "https://dist.nuget.org/win-x86-commandline/v5.4.0/nuget.exe"

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
    Path.Combine(RootDir.FullName, "fsxc", "bin", binaryConfig.ToString())
    |> DirectoryInfo

let RunNugetCommand (command: string) echoMode (safe: bool) =
    if not NugetExe.Exists then
        Console.WriteLine(sprintf "Downloading nuget...")

        if not NugetDir.Exists then
            NugetDir.Create()

        use webClient = new WebClient()
        webClient.DownloadFile(NugetUrl, NugetExe.FullName)

    let nugetCmd =
        match Misc.GuessPlatform() with
        | Misc.Platform.Linux
        | Misc.Platform.Mac ->
            failwith
                "cannot run nuget because this script is not ready for Unix yet"
        | _ ->
            {
                Command = NugetExe.FullName
                Arguments = command
            }

    let proc = Process.Execute(nugetCmd, echoMode)

    if safe then
        proc.UnwrapDefault() |> ignore<string>

    proc

let PrintNugetVersion() =
    if not NugetExe.Exists then
        false
    else
        let nugetProc = RunNugetCommand String.Empty Echo.OutputOnly false

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

let FindBuildTool() =
    match Misc.GuessPlatform() with
    | Misc.Platform.Linux
    | Misc.Platform.Mac ->
        failwith
            "cannot find buildTool because this script is not ready for Unix yet"
    | Misc.Platform.Windows ->
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
        msbuildPath


let BuildSolution
    (buildTool: string)
    (solutionFileName: string)
    (binaryConfig: BinaryConfig)
    (extraOptions: string)
    =
    let configOption = sprintf "/p:Configuration=%s" (binaryConfig.ToString())

    let buildArgs =
        sprintf "%s %s %s" solutionFileName configOption extraOptions

    let buildProcess =
        Process.Execute(
            {
                Command = buildTool
                Arguments = buildArgs
            },
            Echo.All
        )

    match buildProcess.Result with
    | Error _ ->
        Console.WriteLine()
        Console.Out.Flush()
        Console.Error.WriteLine(sprintf "%s build failed ^" buildTool)
        PrintNugetVersion() |> ignore
        Environment.Exit 1
    | _ -> ()

let JustBuild binaryConfig =
    let solFile = "fsx.sln"
    RunNugetCommand (sprintf "restore %s" solFile) Echo.All true |> ignore
    let buildTool = FindBuildTool()

    Console.WriteLine(
        sprintf "Building in %s mode..." (binaryConfig.ToString())
    )

    BuildSolution buildTool solFile binaryConfig String.Empty

let MakeAll() =
    let buildConfig = BinaryConfig.Debug
    JustBuild buildConfig
    buildConfig

let maybeTarget = GatherTarget(Misc.FsxOnlyArguments())

match maybeTarget with

| None
| Some "all" -> MakeAll() |> ignore

| Some "install" ->
    let buildConfig = BinaryConfig.Release
    JustBuild buildConfig

    let programFiles =
        Environment.GetFolderPath Environment.SpecialFolder.ProgramFiles

    let fsxInstallationDir = Path.Combine(programFiles, "fsx") |> DirectoryInfo

    if fsxInstallationDir.Exists then
        failwith "this script can't overwrite an existing installation yet" //TODO

    Console.WriteLine "Installing..."
    Console.WriteLine()

    Misc.CopyDirectoryRecursively(
        mainBinariesDir buildConfig,
        fsxInstallationDir,
        List.Empty
    )

| Some someOtherTarget ->
    Console.Error.WriteLine("Unrecognized target: " + someOtherTarget)
    Environment.Exit 1

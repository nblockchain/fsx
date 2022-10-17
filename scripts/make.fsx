
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
let NugetDir = Path.Combine (RootDir.FullName, ".nuget") |> DirectoryInfo
let NugetExe = Path.Combine (NugetDir.FullName, "nuget.exe") |> FileInfo
let NugetUrl = "https://dist.nuget.org/win-x86-commandline/v5.4.0/nuget.exe"

// because the one from InfraLib seems to hang on Windows (FIXME)
let ProcessExecuteInteractively (exe: string) (args: string) =
    let createdProcess = System.Diagnostics.Process.Start (exe, args)
    createdProcess.WaitForExit()
    createdProcess.ExitCode

type BinaryConfig =
    | Debug
    | Release
    override self.ToString() =
        sprintf "%A" self

let GatherTarget (args: List<string>): Option<string> =
    let rec gatherTarget (args: List<string>) (targetSet: Option<string>): Option<string> =
        match args with
        | [] -> targetSet
        | head::tail ->
            if targetSet.IsSome then
                failwith "only one target can be passed to make"
            gatherTarget tail (Some head)
    gatherTarget args None

let mainBinariesDir binaryConfig =
    Path.Combine (
        RootDir.FullName,
        "fsxc",
        "bin",
        binaryConfig.ToString())
    |> DirectoryInfo

let RunNugetCommand (command: string) echoMode (safe: bool) =
    if not NugetExe.Exists then
        Console.WriteLine (sprintf "Downloading nuget...")
        if not NugetDir.Exists then
            NugetDir.Create()
        use webClient = new WebClient()
        webClient.DownloadFile(NugetUrl, NugetExe.FullName)

    let nugetCmd =  
        match Misc.GuessPlatform() with
        | Misc.Platform.Linux
        | Misc.Platform.Mac ->
            failwith "cannot run nuget because this script is not ready for Unix yet"
        | _ ->
            { Command = NugetExe.FullName; Arguments = command }

    if safe then
        Process.SafeExecute (nugetCmd, echoMode)
    else
        Process.Execute (nugetCmd, echoMode)

let PrintNugetVersion () =
    if not (NugetExe.Exists) then
        false
    else
        let nugetProc = RunNugetCommand String.Empty Echo.Off false
        Console.WriteLine nugetProc.Output.StdOut
        if nugetProc.ExitCode = 0 then
            true
        else
            nugetProc.Output.PrintToConsole()
            Console.WriteLine()
            failwith "nuget process' output contained errors ^"

let ConfigCommandCheck (commandNamesByOrderOfPreference: seq<string>) (exitIfNotFound: bool): Option<string> =
    let rec configCommandCheck currentCommandNamesQueue allCommands =
        match Seq.tryHead currentCommandNamesQueue with
        | Some currentCommand ->
            //Console.Write (sprintf "checking for %s... " currentCommand)
            if not (Process.CommandWorksInShell currentCommand) then
                //Console.WriteLine "not found"
                configCommandCheck (Seq.tail currentCommandNamesQueue) allCommands
            else
                //Console.WriteLine "found"
                currentCommand |> Some
        | None ->
            Console.Error.WriteLine (sprintf "Error, please install %s" (String.Join(" or ", List.ofSeq allCommands)))
            if exitIfNotFound then
                Environment.Exit 1
                failwith "unreachable"
            else
                None

    configCommandCheck commandNamesByOrderOfPreference commandNamesByOrderOfPreference

let FindBuildTool() =
    match Misc.GuessPlatform() with
    | Misc.Platform.Linux | Misc.Platform.Mac ->
        failwith "cannot find buildTool because this script is not ready for Unix yet"
    | Misc.Platform.Windows ->
        //we need to call "%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -find MSBuild\**\Bin\MSBuild.exe

        let programFiles = Environment.GetFolderPath Environment.SpecialFolder.ProgramFilesX86
        let vswhereExe = Path.Combine(programFiles, "Microsoft Visual Studio", "Installer", "vswhere.exe") |> FileInfo
        ConfigCommandCheck (List.singleton vswhereExe.FullName) |> ignore

        let vswhereCmd =
            {
                Command = vswhereExe.FullName  
                Arguments = "-find MSBuild\\**\\Bin\\MSBuild.exe"
            }
        let processResult = Process.Execute(vswhereCmd, Echo.Off)
        if processResult.ExitCode <> 0 then
            processResult.Output.PrintToConsole()
            Console.WriteLine()
            failwith "Some problem when calling vsWhere.exe ^"
        
        let msbuildPath = processResult.Output.StdOut.Trim()
        msbuildPath


let BuildSolution
    (buildTool: string)
    (solutionFileName: string)
    (binaryConfig: BinaryConfig)
    (extraOptions: string)
    =
    let configOption = sprintf "/p:Configuration=%s" (binaryConfig.ToString())
    let buildArgs = sprintf "%s %s %s" solutionFileName configOption extraOptions
    let buildProcess = Process.Execute ({ Command = buildTool; Arguments = buildArgs }, Echo.All)
    if (buildProcess.ExitCode <> 0) then
        buildProcess.Output.PrintToConsole()
        Console.WriteLine()
        Console.Error.WriteLine (sprintf "%s build failed ^" buildTool)
        PrintNugetVersion() |> ignore
        Environment.Exit 1

let JustBuild binaryConfig =
    let solFile = "fsx.sln"
    RunNugetCommand (sprintf "restore %s" solFile) Echo.All true
        |> ignore
    let buildTool = FindBuildTool()
    Console.WriteLine (sprintf "Building in %s mode..." (binaryConfig.ToString()))
    BuildSolution
        buildTool
        solFile
        binaryConfig
        String.Empty

let MakeAll() =
    let buildConfig = BinaryConfig.Debug
    JustBuild buildConfig
    buildConfig

let maybeTarget = GatherTarget (Misc.FsxOnlyArguments())
match maybeTarget with

| None | Some "all" ->
    MakeAll() |> ignore

| Some "install" ->
    let buildConfig = BinaryConfig.Release
    JustBuild buildConfig

    let programFiles = Environment.GetFolderPath Environment.SpecialFolder.ProgramFiles
    let fsxInstallationDir = Path.Combine(programFiles, "fsx") |> DirectoryInfo
    if fsxInstallationDir.Exists then
        failwith "this script can't overwrite an existing installation yet" //TODO

    Console.WriteLine "Installing..."
    Console.WriteLine ()
    Misc.CopyDirectoryRecursively (mainBinariesDir buildConfig, fsxInstallationDir, List.Empty)

| Some someOtherTarget ->
    Console.Error.WriteLine("Unrecognized target: " + someOtherTarget)
    Environment.Exit 1

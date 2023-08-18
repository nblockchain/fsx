#!/usr/bin/env fsx

open System
open System.IO
open System.Net
open System.Linq
open System.Diagnostics

#r "System.Configuration"
open System.Configuration

#load "../Fsdk/Misc.fs"
#load "../Fsdk/Process.fs"
#load "../Fsdk/Network.fs"
#load "../Fsdk/Git.fs"

open Fsdk
open Fsdk.Process

let ScriptsDir = __SOURCE_DIRECTORY__ |> DirectoryInfo
let RootDir = Path.Combine(ScriptsDir.FullName, "..") |> DirectoryInfo
let TestDir = Path.Combine(RootDir.FullName, "test") |> DirectoryInfo
let NugetDir = Path.Combine(RootDir.FullName, ".nuget") |> DirectoryInfo
let NugetExe = Path.Combine(NugetDir.FullName, "nuget.exe") |> FileInfo
let NugetPackages = Path.Combine(RootDir.FullName, "packages") |> DirectoryInfo

let GetFsxWindowsLauncher() =
    let programFiles =
        Environment.GetEnvironmentVariable "ProgramW6432" |> DirectoryInfo

    let fsxWinInstallationDir =
        Path.Combine(programFiles.FullName, "fsx") |> DirectoryInfo

    Path.Combine(fsxWinInstallationDir.FullName, "fsx.bat") |> FileInfo

let CreateCommand(executable: FileInfo, args: string) =
    let platform = Misc.GuessPlatform()

    if (executable.FullName.ToLower().EndsWith(".exe")
        && platform = Misc.Platform.Windows)
       ||
       // because shebang works in Unix
       (executable.FullName.ToLower().EndsWith(".fsx")
        && platform <> Misc.Platform.Windows) then
        {
            Command = executable.FullName
            Arguments = args
        }

    elif
        executable.FullName.ToLower().EndsWith(".fsx")
        && platform = Misc.Platform.Windows
    then
        {
            Command = GetFsxWindowsLauncher().FullName
            Arguments = sprintf "%s %s" executable.FullName args
        }
    elif
        executable.FullName.ToLower().EndsWith(".exe")
        && platform <> Misc.Platform.Windows
    then
        {
            Command = "mono"
            Arguments = sprintf "%s %s" executable.FullName args
        }
    else
        failwith "Unexpected command, you broke 'make check'"


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

let UnwrapDefault(proc: ProcessResult) =
#if !LEGACY_FRAMEWORK
// FIXME: this workaround below is needed because we got warnings in .NET6
    match proc.Result with
    | Error _ ->
        failwithf
            "Process '%s %s' failed"
            proc.Details.Command
            proc.Details.Args
    | _ -> ()
#else
    proc.UnwrapDefault() |> ignore<string>
#endif

let basicTest = Path.Combine(TestDir.FullName, "test.fsx") |> FileInfo

Process.Execute(CreateCommand(basicTest, String.Empty), Echo.All)
|> UnwrapDefault


let ifDefTest = Path.Combine(TestDir.FullName, "testIfDef.fsx") |> FileInfo

Process.Execute(CreateCommand(ifDefTest, String.Empty), Echo.All)
|> UnwrapDefault


let nonExistentTest =
    Path.Combine(TestDir.FullName, "nonExistentFsx.fsx") |> FileInfo

let commandForNonExistentTest =
    if Misc.GuessPlatform() = Misc.Platform.Windows then
        GetFsxWindowsLauncher().FullName
    else
        // FIXME: extract PREFIX from build.config instead of assuming default
        "/usr/local/bin/fsx"

let proc =
    Process.Execute(
        {
            Command = commandForNonExistentTest
            Arguments = nonExistentTest.FullName
        },
        Echo.All
    )

// the reason to write the result of this to a file is:
// if error propagation is broken, then it would be broken as well for make.fsx
// when trying to call this very file (runTests.fsx) and wouldn't pick up an err
let errorPropagationResultFile =
    Path.Combine(TestDir.FullName, "errProp.txt") |> FileInfo

match proc.Result with
| Error _ -> File.WriteAllText(errorPropagationResultFile.FullName, "0")
| _ ->
    File.WriteAllText(errorPropagationResultFile.FullName, "1")
    failwith "Call to non-existent test should have failed (exitCode <> 0)"


let fsFileToBuild = Path.Combine(TestDir.FullName, "test.fs") |> FileInfo
let libToRef1 = Path.Combine(TestDir.FullName, "test1.dll") |> FileInfo
#if !LEGACY_FRAMEWORK
let testLibFsProjContent =
    """
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>

    <!-- the two settings below allow the binaries sit directly in subfolder /bin/ -->
    <OutputPath>.</OutputPath>
    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="test.fs" />
  </ItemGroup>

</Project>
"""

let test1LibFsProj = Path.Combine(TestDir.FullName, "test1.fsproj") |> FileInfo

try
    File.WriteAllText(test1LibFsProj.FullName, testLibFsProjContent)

    let dotnetBuildCmd1 =
        {
            Command = fsharpCompilerCommand
            Arguments = sprintf "build %s" test1LibFsProj.FullName
        }

    Process
        .Execute(dotnetBuildCmd1, Echo.All)
        .UnwrapDefault()
    |> ignore<string>
finally
    test1LibFsProj.Delete()

#else


let fscCmd1 =
    {
        Command = fsharpCompilerCommand
        Arguments =
            sprintf
                "%s --target:library --out:%s"
                fsFileToBuild.FullName
                libToRef1.FullName
    }

Process.Execute(fscCmd1, Echo.All).UnwrapDefault() |> ignore<string>
#endif
let refLibTest = Path.Combine(TestDir.FullName, "testRefLib.fsx") |> FileInfo

Process.Execute(CreateCommand(refLibTest, String.Empty), Echo.All)
|> UnwrapDefault


let subLibFolder =
    Directory.CreateDirectory(Path.Combine(TestDir.FullName, "lib"))

let libToRef2Outside =
    Path.Combine(subLibFolder.FullName, "test2.dll") |> FileInfo
#if !LEGACY_FRAMEWORK
let libToRef2 = Path.Combine(TestDir.FullName, "test2.dll") |> FileInfo
let test2LibFsProj = Path.Combine(TestDir.FullName, "test2.fsproj") |> FileInfo

try
    File.WriteAllText(test2LibFsProj.FullName, testLibFsProjContent)

    let dotnetBuildCmd2 =
        {
            Command = fsharpCompilerCommand
            Arguments = sprintf "build %s" test2LibFsProj.FullName
        }

    Process
        .Execute(dotnetBuildCmd2, Echo.All)
        .UnwrapDefault()
    |> ignore<string>

    File.Copy(libToRef2.FullName, libToRef2Outside.FullName, true)
finally
    test2LibFsProj.Delete()
#else
let fscCmd2 =
    {
        Command = fsharpCompilerCommand
        Arguments =
            sprintf
                "%s --target:library --out:%s"
                fsFileToBuild.FullName
                libToRef2Outside.FullName
    }

Process.Execute(fscCmd2, Echo.All).UnwrapDefault() |> ignore<string>
#endif

let refLibOutsideCurrentFolderTest =
    Path.Combine(TestDir.FullName, "testRefLibOutsideCurrentFolder.fsx")
    |> FileInfo

Process.Execute(
    CreateCommand(refLibOutsideCurrentFolderTest, String.Empty),
    Echo.All
)
|> UnwrapDefault

// this test doesn't make much sense when running dotnet because we would use proper #r "nuget: ..." in that case
#if LEGACY_FRAMEWORK
Network.InstallNugetPackage
    NugetExe
    NugetPackages
    "Microsoft.Build"
    (Some "16.11.0")
    Echo.All
|> ignore

let refNugetLibTest =
    Path.Combine(TestDir.FullName, "testRefNugetLib.fsx") |> FileInfo

Process.Execute(CreateCommand(refNugetLibTest, String.Empty), Echo.All)
|> UnwrapDefault
#endif

// not specifying a version is tricky to convert into a <PackageReference /> so not supported atm
#if LEGACY_FRAMEWORK
let refNugetLibTestNewFormat =
    Path.Combine(TestDir.FullName, "testRefNugetLibNewFormat.fsx") |> FileInfo

Process.Execute(CreateCommand(refNugetLibTestNewFormat, String.Empty), Echo.All)
|> UnwrapDefault
#endif


let refNugetLibTestNewFormatWithVersion =
    Path.Combine(TestDir.FullName, "testRefNugetLibNewFormatWithVersion.fsx")
    |> FileInfo

Process.Execute(
    CreateCommand(refNugetLibTestNewFormatWithVersion, String.Empty),
    Echo.All
)
|> UnwrapDefault

let refNugetLibTestNewFormatWithShortVersion =
    Path.Combine(
        TestDir.FullName,
        "testRefNugetLibNewFormatWithShortVersion.fsx"
    )
    |> FileInfo

Process.Execute(
    CreateCommand(refNugetLibTestNewFormatWithShortVersion, String.Empty),
    Echo.All
)
|> UnwrapDefault


let cmdLineArgsTest =
    Path.Combine(TestDir.FullName, "testFsiCommandLineArgs.fsx") |> FileInfo

Process.Execute(CreateCommand(cmdLineArgsTest, "one 2 three"), Echo.All)
|> UnwrapDefault

let processTest = Path.Combine(TestDir.FullName, "testProcess.fsx") |> FileInfo

Process.Execute(CreateCommand(processTest, String.Empty), Echo.All)
|> UnwrapDefault


(* this is actually only really useful for when process spits both stdout & stderr
let processConcurrencyTest = Path.Combine(TestDir.FullName, "testProcessConcurrency.fsx") |> FileInfo
Process.Execute({ Command = processConcurrencyTest.FullName; Arguments = String.Empty }, Echo.All)
       .UnwrapDefault() |> ignore<string>
*)


let legacyDefineTest =
    Path.Combine(
        TestDir.FullName,
#if !LEGACY_FRAMEWORK
        "testNonLegacyFx.fsx"
#else
        "testLegacyFx.fsx"
#endif
    )
    |> FileInfo

Process.Execute(CreateCommand(legacyDefineTest, String.Empty), Echo.All)
|> UnwrapDefault


let contentOfScriptWithWarning =
    """#!/usr/bin/env fsx

let GiveMeBool() : bool =
    false

GiveMeBool()
printf "hello"
"""

let warningTest = Path.Combine(TestDir.FullName, "testWarning.fsx") |> FileInfo
File.WriteAllText(warningTest.FullName, contentOfScriptWithWarning)

match Misc.GuessPlatform() with
| Misc.Platform.Windows -> ()
| _ ->
    Process
        .Execute(
            {
                Command = "chmod"
                Arguments = sprintf "+x %s" warningTest.FullName
            },
            Echo.All
        )
        .UnwrapDefault()
    |> ignore<string>

let currentDir = Directory.GetCurrentDirectory()

let possibleDirBuildProps =
    Path.Combine(currentDir, "Directory.Build.props") |> FileInfo

if possibleDirBuildProps.Exists then
    // this file could alter the behaviour of fsxc when compiling, making the result of the test be misleading
    possibleDirBuildProps.Delete()

let warningAsErrorProc =
    Process.Execute(CreateCommand(warningTest, String.Empty), Echo.All)

match warningAsErrorProc.Result with
| Error _ ->
    // warning as error worked!
    ()
| _ ->
    failwithf
        "Should have failed to compile/execute %s because warnings as errors"
        warningTest.Name

warningTest.Delete()

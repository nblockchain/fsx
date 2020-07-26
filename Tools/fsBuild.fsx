#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq
open System.Xml
#r "System.Xml.Linq"
open System.Xml.Linq
open System.Xml.XPath
#r "System.Configuration"
open System.Configuration
#load "../InfraLib/Misc.fs"
#load "../InfraLib/Process.fs"
open FSX.Infrastructure
open Process

if Misc.FsxArguments().Length <> 1 then
    Console.Error.WriteLine "Only one argument is supported; not less, not more"
    Environment.Exit 1

let arg = Misc.FsxArguments().[0]
if not (arg.EndsWith ".fsproj") then
    Console.Error.WriteLine "File format not supported; only .fsproj can be used with fsBuild for now"
    Environment.Exit 1

let projFile = Path.Combine(Directory.GetCurrentDirectory(), arg) |> FileInfo
if not projFile.Exists then
    Console.Error.WriteLine (sprintf "File not found: %s" projFile.FullName)
    Environment.Exit 1

let private defaultNugetPkgFolder =
    Path.Combine(Environment.GetFolderPath Environment.SpecialFolder.Personal, ".nuget", "packages")
    |> DirectoryInfo

type Referencer =
    | Project
    | Package of Package

and Package =
    {
        Id: string
        Version: Misc.SpecVersion
        Referencer: Referencer
        NetStandardVersion: Option<string>
    }
    member self.NugetFolder =
        let res = Path.Combine(defaultNugetPkgFolder.FullName, self.Id.ToLower(), self.Version.ToString())
                  |> DirectoryInfo
        if not res.Exists then
            failwithf "%s dir does not exist" res.FullName
        res
    member private self.NugetLibFolder =
        Path.Combine(self.NugetFolder.FullName, "lib")
        |> DirectoryInfo
    member private self.NugetRefFolder =
        Path.Combine(self.NugetFolder.FullName, "ref")
        |> DirectoryInfo
    static member CompatibleNetStandardVersions =
        seq {
            yield "2.0"
            yield "1.3"
            yield "1.1"
            yield "1.0"
        }
    member self.NuspecFile =
        let res = Path.Combine(self.NugetFolder.FullName, sprintf "%s.nuspec" (self.Id.ToLower()))
                  |> FileInfo
        if not res.Exists then
            failwithf "%s dir does not exist" res.FullName
        res
    member self.NugetFoldersForNetStandardBinaries preferredNetStandardVersionOpt =
        seq {
            match preferredNetStandardVersionOpt with
            | None ->
                ()
            | Some preferredNetStandardVersion ->
                let preferredVersionFolder =
                    Path.Combine(self.NugetRefFolder.FullName, sprintf "netstandard%s" preferredNetStandardVersion)
                    |> DirectoryInfo
                if preferredVersionFolder.Exists then
                    yield preferredVersionFolder

                let preferredVersionFolderAlt =
                    Path.Combine(self.NugetLibFolder.FullName, sprintf "netstandard%s" preferredNetStandardVersion)
                    |> DirectoryInfo
                if preferredVersionFolderAlt.Exists then
                    yield preferredVersionFolderAlt

            for netstandardVersion in Package.CompatibleNetStandardVersions do
                yield Path.Combine(self.NugetRefFolder.FullName, sprintf "netstandard%s" netstandardVersion)
                      |> DirectoryInfo
                yield Path.Combine(self.NugetLibFolder.FullName, sprintf "netstandard%s" netstandardVersion)
                      |> DirectoryInfo
        }

type Project =
    {
        Files: seq<string>
        EmbeddedResources: seq<string>
        PackageReferences: seq<Package>
    }

module XmlParser =
    let CheckTargetFramework() =
        let xmlContents = File.ReadAllText projFile.FullName
        let xDoc = XDocument.Parse xmlContents
        let targetFrameworkNode = xDoc.XPathSelectElement "//Project/PropertyGroup/TargetFramework"
        if targetFrameworkNode = null || targetFrameworkNode.Value <> "netstandard2.0" then
            Console.WriteLine "Incompatible project file: doesn't look like it's a .NETStandard2.0 project"
            Environment.Exit 1
        xDoc

    let ParseNuspecFile (nuspec: FileInfo) =
        let xmlContents = File.ReadAllText nuspec.FullName
        XDocument.Parse xmlContents

    let FindNuspecNetStandard20Dependencies (pkg: Package) (xDoc: XDocument) =
        let findDependencies (nsOpt: Option<XmlNamespaceManager*string>) =
            let rec findDependencyGroup netStandardVersions =
                match Seq.tryHead netStandardVersions with
                | None ->
                    None
                | Some netStandardVersion ->
                    let groupQuery =
                        sprintf "//{0}package/{0}metadata/{0}dependencies/{0}group[@targetFramework='.NETStandard%s']"
                                netStandardVersion
                    let groupNodes =
                        match nsOpt with
                        | None ->
                            let fixedGroupQuery = String.Format(groupQuery, String.Empty)
                            xDoc.XPathSelectElements fixedGroupQuery
                        | Some(nsManager, nsPrefix) ->
                            let fixedGroupQuery = String.Format(groupQuery, nsPrefix)
                            xDoc.XPathSelectElements(fixedGroupQuery, nsManager)
                    if groupNodes.Any() then
                        Some netStandardVersion
                    else
                        findDependencyGroup (Seq.tail netStandardVersions)

            let maybeNetStandardVersionFound = findDependencyGroup Package.CompatibleNetStandardVersions
            match maybeNetStandardVersionFound with
            | None -> Seq.empty
            | Some netStandardVersion ->
                let depsQuery = sprintf
                                    "//{0}package/{0}metadata/{0}dependencies/{0}group[@targetFramework='.NETStandard%s']/{0}dependency"
                                    netStandardVersion
                let depNodes =
                    match nsOpt with
                    | None ->
                        let fixedDepsQuery = String.Format(depsQuery, String.Empty)
                        xDoc.XPathSelectElements fixedDepsQuery
                    | Some(nsManager, nsPrefix) ->
                        let fixedDepsQuery = String.Format(depsQuery, nsPrefix)
                        xDoc.XPathSelectElements(fixedDepsQuery, nsManager)
                seq {
                    for dependencyNode in depNodes do

                        let id = (dependencyNode.Attribute (XName.op_Implicit "id")).Value
                        let version = (dependencyNode.Attribute (XName.op_Implicit "version")).Value
                        let excludeAttrib = dependencyNode.Attribute (XName.op_Implicit "exclude")
                        if excludeAttrib = null || (not (excludeAttrib.Value.ToLower().Contains "compile")) then
                            yield
                                {
                                    Id = id
                                    Version = Misc.SpecVersion.Parse version
                                    Referencer = Package pkg
                                    NetStandardVersion = Some netStandardVersion
                                }
                }

        let nsOpt =
            let nsString = xDoc.Root.Name.Namespace.ToString()
            if String.IsNullOrEmpty nsString then
                None
            else
                let xsdUrl1 = "http://schemas.microsoft.com/packaging/2012/06/nuspec.xsd"
                let xsdUrl2 = "http://schemas.microsoft.com/packaging/2013/01/nuspec.xsd"
                let xsdUrl3 = "http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd"
                if nsString <> xsdUrl1 && nsString <> xsdUrl2 && nsString <> xsdUrl3 then
                    failwithf "Unexpected XMLns?: %s" nsString
                let nsManager = XmlNamespaceManager(NameTable())
                let nsPrefix = "x"
                nsManager.AddNamespace(nsPrefix, nsString)
                Some(nsManager, sprintf "%s:" nsPrefix)

        findDependencies nsOpt


    let ParseProjectFile (): Project =
        let xDoc = CheckTargetFramework()
        let fsFileNodes = xDoc.XPathSelectElements "//Project/ItemGroup/Compile"
        let fsFiles =
            seq {
                for fsFile in fsFileNodes do
                    yield (fsFile.Attribute (XName.op_Implicit "Include")).Value
            }

        let resourceNodes = xDoc.XPathSelectElements "//Project/ItemGroup/EmbeddedResource"
        let resourceFiles =
            seq {
                for resourceFile in resourceNodes do
                    yield (resourceFile.Attribute (XName.op_Implicit "Include")).Value
            }


        let pkgNodes = xDoc.XPathSelectElements  "//Project/ItemGroup/PackageReference"
        let pkgs =
            seq {
                for pkgNode in pkgNodes do
                    let includeAttr = pkgNode.Attribute (XName.op_Implicit "Include")
                    let updateAttr = pkgNode.Attribute (XName.op_Implicit "Update")
                    let name =
                        if includeAttr <> null then
                            includeAttr.Value
                        elif updateAttr <> null then
                            updateAttr.Value
                        else
                            failwith "Neither 'Include' nor 'Update' attrib were found in this PackageReference element..."

                    let versionAttr = pkgNode.Attribute (XName.op_Implicit "Version")
                    if versionAttr = null then
                        failwith "No 'Version' attrib found in PackageReferece"
                    yield
                        {
                            Id = name
                            Version = Misc.SpecVersion.Parse versionAttr.Value
                            Referencer = Project
                            NetStandardVersion = None
                        }
            }

        {
            Files = fsFiles
            EmbeddedResources = resourceFiles
            PackageReferences = pkgs
        }

module Builder =

    let private GetConfig (debug: bool) =
        if debug then
            "Debug"
        else
            "Release"

    let GetDefaultFlags (debug: bool) (projFileNameWithoutExtension) =
        seq {
            yield "-o", Some <| sprintf "obj/%s/netstandard2.0/%s.dll" (GetConfig debug) projFileNameWithoutExtension
            yield "-g", None
            yield "--noframework", None
            if debug then
                yield "--optimize-", None
                yield "--tailcalls-", None
                yield "--debug", Some "portable"
                yield "--define", Some "TRACE"
                yield "--define", Some "DEBUG"
            yield "--define", Some "NETSTANDARD"
            yield "--define", Some "NETSTANDARD2_0"

            yield "--targetprofile", Some "netstandard"
            // if it was not a library, its targetFramework would be netcoreappX.Y, which we don't support
            yield "--target", Some "library"

            yield "--warn", Some "3"
            yield "--warnaserror", Some "76"
            yield "--fullpaths", None
            yield "--flaterrors", None
            yield "--highentropyva+", None
            yield "--simpleresolution", None
            yield "--nocopyfsharpcore", None
        }

    let Prepare (debug: bool) =
        let netStandard2File =
            Path.Combine(projFile.Directory.FullName,
                         "obj",
                         GetConfig debug,
                         "netstandard2.0",
                         ".NETStandard,Version=v2.0.AssemblyAttributes.fs")
            |> FileInfo
        if not netStandard2File.Directory.Exists then
            netStandard2File.Directory.Create()
        let netStandard2Attrib = """namespace Microsoft.BuildSettings

[<System.Runtime.Versioning.TargetFrameworkAttribute(".NETStandard,Version=v2.0", FrameworkDisplayName="")>]
do ()
"""
        File.WriteAllText(netStandard2File.FullName, netStandard2Attrib)
        netStandard2File

    let ExpandReferencesAddingSubDependencies (pkgRefs: seq<Package>) (netStandardRefs: seq<string>): Map<string, Package> =
        let indexPkgs (pkgs: seq<Package>): seq<string*Package> =
            Seq.map (fun pkgRef -> pkgRef.Id, pkgRef) pkgs
        let rec expandInner (current: seq<string*Package>) (acc: Map<string, Package>)
                            : Map<string, Package> =
            let rec maybeReplaceDepWithHigherVersion (subDeps: seq<Package>)
                                                     (firstTimeSeenDeps: List<Package>)
                                                     (theMap: Map<string, Package>)
                                                     : Map<string, Package>*List<Package> =
                match Seq.tryHead subDeps with
                | None -> theMap,firstTimeSeenDeps
                | Some subDep ->
                    let maybeExistingDep = Map.tryFind subDep.Id theMap
                    let newMap,firstTimeDep =
                        match maybeExistingDep with
                        | None ->
                            if not (netStandardRefs.Any(fun nsRef -> nsRef.ToLower() = subDep.Id.ToLower())) then
                                theMap.Add(subDep.Id, subDep),true
                            else
                                theMap,false
                        | Some existingDep ->
                            if subDep.Version > existingDep.Version then
                                theMap.Add(subDep.Id, subDep),false
                            else
                                theMap,false
                    let newFirstTimeDeps =
                        if firstTimeDep then
                            subDep::firstTimeSeenDeps
                        else
                            firstTimeSeenDeps
                    maybeReplaceDepWithHigherVersion (Seq.tail subDeps) newFirstTimeDeps newMap

            match Seq.tryHead current with
            | None -> acc
            | Some (pkgId, pkg) ->
                let parsedNuspecFile = XmlParser.ParseNuspecFile pkg.NuspecFile
                let deps = XmlParser.FindNuspecNetStandard20Dependencies pkg parsedNuspecFile
                let newAcc,notExistentDepsInAcc = maybeReplaceDepWithHigherVersion deps List.Empty acc
                let rest = Seq.append (Seq.tail current) (indexPkgs notExistentDepsInAcc)
                expandInner rest newAcc

        let initialMap = Seq.map (fun pkgRef -> pkgRef.Id, pkgRef) pkgRefs |> Map.ofSeq
        expandInner (indexPkgs pkgRefs) initialMap

    let FindReferences (pkgRefs: seq<Package>) =
        let rec findFilesFromRefs (pkgs: Map<string, Package>) =
            seq {
                for KeyValue(pkgId, pkg) in pkgs do
                    let maybePkgDir =
                        Seq.tryFind (fun (dir: DirectoryInfo) -> dir.Exists)
                                    (pkg.NugetFoldersForNetStandardBinaries pkg.NetStandardVersion)
                    match maybePkgDir with
                    | None ->
                        match pkg.Referencer with
                        | Project ->
                            Console.Error.Write
                                (sprintf "WARNING: Dependency '%s(%s)' not met. "
                                         pkgId (pkg.Version.ToString()))
                        | Package parent ->
                            Console.Error.Write
                                (sprintf "WARNING: Dependency '%s(%s)' of package '%s(%s)' not met. "
                                         pkgId (pkg.Version.ToString()) parent.Id (parent.Version.ToString()))
                        Console.Error.WriteLine
                            "Did nuget restore not work or is this dependency not NetStandard2.0 compatible?"
                        Environment.Exit 1
                    | Some pkgDir ->
                        for file in pkgDir.EnumerateFiles() do
                            if file.FullName.EndsWith ".dll"

                               // I've found this lib from FSharp.Data to not referenced by the consumers of the nuget
                               // package, and I don't know how they blacklist it... maybe by not including it in the
                               // <references> list in the nuspec file? e.g.:
                               //   <references><reference file="FSharp.Data.dll" /></references>
                               && (not (file.FullName.EndsWith "FSharp.Data.DesignTime.dll"))

                               then
                                yield file
            }
        let netStandardLibsDir =
            Path.Combine(defaultNugetPkgFolder.FullName,
                         "netstandard.library",
                         "2.0.3",
                         "build",
                         "netstandard2.0",
                         "ref")
            |> DirectoryInfo

        let netStandardRefs =
            seq {
                for file in netStandardLibsDir.EnumerateFiles() do
                    if file.FullName.ToLower().EndsWith ".dll" then
                        yield file
            } |> Seq.sortBy (fun file -> file.FullName)

        let allDeps = ExpandReferencesAddingSubDependencies
                          pkgRefs
                          (netStandardRefs.Select(fun file -> Path.GetFileNameWithoutExtension file.FullName))
        seq {
            for nsRef in netStandardRefs do
                yield nsRef
            for file in findFilesFromRefs allDeps do
                yield file
        }

let parsedProject = XmlParser.ParseProjectFile()

let fscPath =
    match Misc.GuessPlatform() with
    | Misc.Platform.Mac -> "/Library/Frameworks/Mono.framework/Versions/6.10.0/lib/mono/fsharp/"
    | Misc.Platform.Linux -> "/usr/lib/mono/fsharp/"
    | _ -> failwith "Platform not supported"
let fscCommand = Path.Combine(fscPath, "fsc.exe")

let debug = true
let attribFile = Builder.Prepare debug

let projFileNameWithoutExtension = Path.GetFileNameWithoutExtension projFile.FullName
let flags = Builder.GetDefaultFlags debug projFileNameWithoutExtension
            |> List.ofSeq
let refs = Builder.FindReferences parsedProject.PackageReferences
           |> List.ofSeq

Console.WriteLine fscCommand
for (flag, valueOpt) in flags do
    match valueOpt with
    | None ->
        Console.WriteLine (sprintf "  %s" flag)
    | Some value ->
        Console.WriteLine (sprintf "  %s:%s" flag value)
for embeddedResource in parsedProject.EmbeddedResources do
    let unixPath = embeddedResource.Replace ("\\", "/")
    let justFileName = Path.GetFileName unixPath
    Console.WriteLine (sprintf "  --resource:%s,%s.%s" unixPath projFileNameWithoutExtension justFileName)
for ref in refs do
    Console.WriteLine (sprintf "  -r:%s" ref.FullName)
Console.WriteLine (sprintf "  %s" attribFile.FullName)
for fsFile in parsedProject.Files do
    let unixPath = fsFile.Replace ("\\", "/")
    Console.WriteLine (sprintf "  %s" unixPath)


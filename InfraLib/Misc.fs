namespace FSX.Infrastructure

open System
open System.IO
open System.Reflection
open System.Configuration
open System.Linq
open System.Security.Cryptography

module Misc =

    let private FileMatchesIfArgumentIsAPath(argument: string, file: FileInfo) =
        try
            FileInfo(argument).FullName.Equals(file.FullName)
        with
        | _ -> false

    let private ExtensionMatchesIfArgumentIsAPath
        (
            argument: string,
            extension: string
        ) =
        try
            Path
                .GetFileName(argument)
                .EndsWith("." + extension)
        with
        | _ -> false

    // this below is crazy but is to avoid # char being ignored in Uri.LocalPath property, see https://stackoverflow.com/a/41203269
    let private currentExeUri =
        Uri(Uri.EscapeUriString(Assembly.GetEntryAssembly().CodeBase))

    let private currentExe =
        FileInfo(
            sprintf
                "%s%s"
                (Uri.UnescapeDataString(currentExeUri.PathAndQuery))
                (Uri.UnescapeDataString(currentExeUri.Fragment))
        )

    let rec private FsxOnlyArgumentsInternalFsx(args: list<string>) =
        match args with
        | [] -> []
        | head :: tail ->
            if FileMatchesIfArgumentIsAPath(head, currentExe) then
                tail
            else
                FsxOnlyArgumentsInternalFsx(tail)

    let rec private FsxOnlyArgumentsInternalFsi
        (
            args: list<string>,
            fsxFileFound: bool
        ) =
        match args with
        | [] -> []
        | head :: tail ->
            match fsxFileFound with
            | false ->
                if ExtensionMatchesIfArgumentIsAPath(head, "fsx") then
                    FsxOnlyArgumentsInternalFsi(tail, true)
                else
                    FsxOnlyArgumentsInternalFsi(tail, false)
            | true ->
                if (head.Equals("--")) then
                    tail
                else
                    args

    let FsxOnlyArguments() =
        let cmdLineArgs = Environment.GetCommandLineArgs() |> List.ofSeq
#if !LEGACY_FRAMEWORK
        List.skip 2 cmdLineArgs
#else
        let isFsi =
            String.Equals(
                currentExe.Name,
                "fsi.exe",
                StringComparison.OrdinalIgnoreCase
            )

        if isFsi then
            // XXX: deprecate in favor of fsi.CommandLineArgs? see https://docs.microsoft.com/en-us/dotnet/articles/fsharp/tutorials/fsharp-interactive/
            FsxOnlyArgumentsInternalFsi(cmdLineArgs, false)

        // below for #!/usr/bin/fsx shebang
        else
            FsxOnlyArgumentsInternalFsx(cmdLineArgs)
#endif

    type private SupportedCheckSumAlgorithm =
        | MD5
        | SHA256

    let private ComputeHash(algo: SupportedCheckSumAlgorithm, stream: Stream) =
        match algo with
        | SupportedCheckSumAlgorithm.MD5 ->
            use md5 = System.Security.Cryptography.MD5.Create()
            md5.ComputeHash(stream)
        | SupportedCheckSumAlgorithm.SHA256 ->
            use sha256 = System.Security.Cryptography.SHA256.Create()
            sha256.ComputeHash(stream)

    let private CalculateSum(algo: SupportedCheckSumAlgorithm, file: FileInfo) =
        file.Refresh()

        if not(file.Exists) then
            raise <| FileNotFoundException("File not found", file.FullName)

        use stream = File.OpenRead(file.FullName)
        let bytes = ComputeHash(algo, stream)

        BitConverter
            .ToString(bytes)
            .Replace("-", String.Empty)
            .ToLower()

    let CalculateMD5(file: FileInfo) : string =
        CalculateSum(SupportedCheckSumAlgorithm.MD5, file)

    let CalculateSHA256(file: FileInfo) : string =
        CalculateSum(SupportedCheckSumAlgorithm.SHA256, file)

    let FirstElementOf3Tuple(a, _, _) =
        a

    let SecondElementOf3Tuple(_, b, _) =
        b

    let CrossPlatformStringSplitInLines(str: string) : List<string> =
        let sanitizedStr = str.Replace("\r\n", "\n")

        List.ofSeq(
            sanitizedStr.Split(
                [| "\n" |],
                StringSplitOptions.RemoveEmptyEntries
            )
        )

    let SimpleStringSplit(str: string, separator: string) : list<string> =
        List.ofSeq(
            str.Split([| separator |], StringSplitOptions.RemoveEmptyEntries)
        )

    type Platform =
        | Windows
        | Linux
        | Mac

    // FIXME: maybe get rid of this method when we migrate to .NET6 (or earlier)
    // because it seems it's unnecessary (e.g. see https://gitlab.com/nblockchain/geewallet/-/merge_requests/140 )
    let GuessPlatform() =
        let macDirs =
            [
                "/Applications"
                "/System"
                "/Users"
                "/Volumes"
            ]

        match Environment.OSVersion.Platform with
        | PlatformID.MacOSX -> Platform.Mac
        | PlatformID.Unix ->
            if (macDirs.All(fun dir -> Directory.Exists(dir))) then
                Platform.Mac
            else
                Platform.Linux
        | _ -> Platform.Windows

    let GetAllFilesRecursively(dir: DirectoryInfo) =
        Directory.GetFiles(dir.FullName, "*.*", SearchOption.AllDirectories)

    // even if this function could be more correct by doing case-sensitive comparison in Linux (as
    // opposed to Mac/Windows), it's only being used for now by excludeBasePaths below, and we want
    // that function to match the whitelist in a case-insensitive way in any case (any platform)
    let private IsPathEqual(a: string, b: string) =
        a.Equals(b, StringComparison.InvariantCultureIgnoreCase)

    let ApplyLineChangesOverTextFile
        (fromFile: FileInfo)
        (toFile: FileInfo)
        (change: string -> Option<string>)
        : unit =
        if not fromFile.Exists then
            failwithf "File %s doesn't exist" fromFile.FullName

        if toFile.Exists then
            failwithf "File %s already exists" toFile.FullName

        use writeStream = new StreamWriter(toFile.FullName)

        for line in File.ReadLines fromFile.FullName do
            match change line with
            | None -> ()
            | Some lineChanged -> writeStream.WriteLine lineChanged

    let private CopyToOverwrite
        (
            from: FileInfo,
            toPath: string,
            overwrite: bool
        ) =
        try
            if overwrite then
                from.CopyTo(toPath, true) |> ignore
            else
                from.CopyTo(toPath) |> ignore
        with
        | _ ->
            Console.Error.WriteLine(
                "Error while trying to copy {0} to {1}",
                from.FullName,
                toPath
            )

            reraise()

    let rec CopyDirectoryRecursively
        (
            sourceDir: DirectoryInfo,
            targetDir: DirectoryInfo,
            excludeBasePaths: seq<string>
        ) =
        sourceDir.Refresh()

        if not(sourceDir.Exists) then
            raise
            <| ArgumentException(
                "Source directory does not exist: " + targetDir.FullName,
                "sourceDir"
            )

        targetDir.Refresh()

        if targetDir.Exists then
            raise
            <| ArgumentException(
                "Target directory already exists: " + targetDir.FullName,
                "targetDir"
            )

        Directory.CreateDirectory(targetDir.FullName) |> ignore

        for sourceFile in sourceDir.GetFiles() do
            if (excludeBasePaths.Any(fun x ->
                IsPathEqual(
                    Path.Combine(sourceDir.FullName, x),
                    sourceFile.FullName
                )
            )) then
                ()
            else
                CopyToOverwrite(
                    sourceFile,
                    Path.Combine(targetDir.FullName, sourceFile.Name),
                    true
                )

        for sourceSubFolder in sourceDir.GetDirectories() do
            if (excludeBasePaths.Any(fun x ->
                IsPathEqual(
                    Path.Combine(sourceDir.FullName, x),
                    sourceSubFolder.FullName
                )
            )) then
                ()
            else
                CopyDirectoryRecursively(
                    sourceSubFolder,
                    DirectoryInfo(
                        Path.Combine(targetDir.FullName, sourceSubFolder.Name)
                    ),
                    []
                )

    let rec private SyncCopyDirectoryRecursively
        (
            sourceDir: DirectoryInfo,
            targetDir: DirectoryInfo,
            overwrite: bool,
            excludeBasePaths: seq<string>
        ) =
        sourceDir.Refresh()

        if not(sourceDir.Exists) then
            raise
            <| ArgumentException(
                "Source directory does not exist: " + targetDir.FullName,
                "sourceDir"
            )

        targetDir.Refresh()

        if not(targetDir.Exists) then
            CopyDirectoryRecursively(sourceDir, targetDir, excludeBasePaths)

        else
            for sourceSubFolder in sourceDir.GetDirectories() do
                if (excludeBasePaths.Any(fun x ->
                    IsPathEqual(
                        Path.Combine(sourceDir.FullName, x),
                        sourceSubFolder.FullName
                    )
                )) then
                    ()
                else
                    let targetFolder =
                        DirectoryInfo(
                            Path.Combine(
                                targetDir.FullName,
                                sourceSubFolder.Name
                            )
                        )

                    let subExcludePaths = [] // empty because arg is called exclude*Base*Paths

                    SyncCopyDirectoryRecursively(
                        sourceSubFolder,
                        targetFolder,
                        overwrite,
                        subExcludePaths
                    )

            for sourceFile in sourceDir.GetFiles() do
                if (excludeBasePaths.Any(fun x ->
                    IsPathEqual(
                        Path.Combine(sourceDir.FullName, x),
                        sourceFile.FullName
                    )
                )) then
                    ()
                else
                    let destFile =
                        Path.Combine(targetDir.FullName, sourceFile.Name)

                    if not(File.Exists(destFile)) then
                        CopyToOverwrite(sourceFile, destFile, false)
                    else if overwrite then
                        CopyToOverwrite(sourceFile, destFile, true)
                    else
                        ()

    let private SafeDeleteFile(file: FileInfo) =
        file.Refresh()

        if file.Exists then
            try
                if (GuessPlatform() = Platform.Windows) then
                    File.SetAttributes(file.FullName, FileAttributes.Normal)

                file.Delete()
            with
            | ex ->
                raise
                <| Exception(
                    sprintf
                        "Could not delete file '%s' for some reason, maybe the file is open by some app?"
                        file.FullName,
                    ex
                )

    let rec private SafeDeleteFilesOfDirRecursively(dir: DirectoryInfo) =
        dir.Refresh()

        if dir.Exists then
            for subDir in dir.GetDirectories() do
                SafeDeleteFilesOfDirRecursively(subDir)

            for file in dir.GetFiles() do
                SafeDeleteFile(file)

    let rec private SafeDeleteDirRecursively(dir: DirectoryInfo) =
        dir.Refresh()

        if dir.Exists then
            try
                SafeDeleteFilesOfDirRecursively(dir)

                for subDir in dir.GetDirectories() do
                    SafeDeleteDirRecursively(subDir)

                dir.Delete(true)
            with
            | ex ->
                raise
                <| Exception(
                    sprintf
                        "Could not delete dir '%s' for some reason, maybe the folder is open in Windows Explorer?"
                        dir.FullName,
                    ex
                )

    let rec private SyncDeleteDirectoryRecursively
        (
            sourceDir: DirectoryInfo,
            targetDir: DirectoryInfo
        ) =
        targetDir.Refresh()

        if not(targetDir.Exists) then
            invalidArg "targetDir" "targetDir doesn't exist"

        for targetSubFolder in targetDir.GetDirectories() do
            let hypotheticalEquivalentSourceFolder =
                DirectoryInfo(
                    Path.Combine(sourceDir.FullName, targetSubFolder.Name)
                )

            if hypotheticalEquivalentSourceFolder.Exists then
                SyncDeleteDirectoryRecursively(
                    hypotheticalEquivalentSourceFolder,
                    targetSubFolder
                )

        for targetSubFolder in targetDir.GetDirectories() do
            let hypotheticalEquivalentSourceFolder =
                DirectoryInfo(
                    Path.Combine(sourceDir.FullName, targetSubFolder.Name)
                )

            if not(hypotheticalEquivalentSourceFolder.Exists) then
                SafeDeleteDirRecursively(targetSubFolder)

        for targetFile in targetDir.GetFiles() do
            let hypotheticalEquivalentSourceFile =
                FileInfo(Path.Combine(sourceDir.FullName, targetFile.Name))

            if not(hypotheticalEquivalentSourceFile.Exists) then
                SafeDeleteFile(targetFile)

    // safe means:
    //  1) copy everything from deeper folders to less deeper folders, recursively, without overwriting files
    //  2) copy everything (again) from deeper folders to less deeper folders, recursively, overwriting files
    //  3) delete what is not needed (also recursively) so that it's ideal operation for minimal-downtime deployment
    // this takes more time because it's potentially copying some files twice, but it's safer
    let rec SafeSyncDirectoryRecursively
        (
            sourceDir: DirectoryInfo,
            targetDir: DirectoryInfo,
            excludeBasePaths: seq<string>
        ) =
        sourceDir.Refresh()

        if not(sourceDir.Exists) then
            invalidArg
                "sourceDir"
                (sprintf "Source directory doesn't exist: %s" sourceDir.FullName)

        for excludePath in excludeBasePaths do
            let fullPathOfExcludePath =
                Path.Combine(sourceDir.FullName, excludePath)

            if ((not(Directory.Exists(fullPathOfExcludePath)))
                && (not(File.Exists(fullPathOfExcludePath)))) then
                invalidArg
                    "excludeBasePaths"
                    (String.Format(
                        "ExcludePath {0} doesn't exist",
                        fullPathOfExcludePath
                    ))

        SyncCopyDirectoryRecursively(
            sourceDir,
            targetDir,
            false,
            excludeBasePaths
        )

        SyncCopyDirectoryRecursively(
            sourceDir,
            targetDir,
            true,
            excludeBasePaths
        )

        SyncDeleteDirectoryRecursively(sourceDir, targetDir)


    let private DEFAULT_SEPARATORS_ACCEPTED_IN_TSV_PARSER = [| "\t" |]

    let private SplitRowInKeyAndValue
        (
            tsvRow: string,
            seps: array<string>
        ) : (string * string) =
        let elements = tsvRow.Split(seps, StringSplitOptions.RemoveEmptyEntries)

        if not(elements.Length = 2) then
            failwith("Expecting only key and value per row")

        elements.[0], elements.[1]

    let private TsvParseImplementation
        (
            tsv: string,
            seps: array<string>
        ) : Map<string, string> =
        let rows = CrossPlatformStringSplitInLines tsv
        let rowCount = rows.Length

        if (rowCount < 2) then
            failwith(
                sprintf
                    "Row count has to be at least 2 for this TSV parser, got %d"
                    rowCount
            )

        let firstRow = rows.First()

        let elementsOfFirstRow =
            firstRow.Split(seps, StringSplitOptions.RemoveEmptyEntries)

        let columnCount = elementsOfFirstRow.Length

        if (columnCount = 2) then
            [
                for row in rows -> SplitRowInKeyAndValue(row, seps)
            ]
            |> Map.ofSeq
        else if not(rows.Length = 2) then
            failwith(
                "This TSV parser only accepts keys and values, so either number of rows has to be 2, or number of columns"
            )
        else
            let elementsOfSecondRow =
                rows
                    .ElementAt(1)
                    .Split(seps, StringSplitOptions.None)

            let secondRowColumnCount = elementsOfSecondRow.Length

            if not(columnCount = secondRowColumnCount) then
                failwith(
                    sprintf
                        "Both rows should have same column counts but got %d and %d for 1st and 2nd respectively"
                        columnCount
                        secondRowColumnCount
                )

            [
                for column in [ 0 .. columnCount - 1 ] ->
                    let key = elementsOfFirstRow.[column]
                    let value = elementsOfSecondRow.[column]
                    key, value
            ]
            |> Map.ofSeq

    let TsvParseWithSeparator(tsv: string, sep: string) : Map<string, string> =
        try
            TsvParseImplementation(tsv, [| sep |])
        with
        | ex -> raise <| Exception("TSV failed, input: " + tsv, ex)

    let TsvParse(tsv: string) : Map<string, string> =
        try
            TsvParseImplementation(
                tsv,
                DEFAULT_SEPARATORS_ACCEPTED_IN_TSV_PARSER
            )
        with
        | ex -> raise <| Exception("TSV failed, input: " + tsv, ex)

    let ConsoleReadPasswordLine() =

        // taken from http://stackoverflow.com/questions/3404421/password-masking-console-application
        let rec ConsoleReadPasswordLineInternal(pwd: string) =
            let key = Console.ReadKey(true)

            if (key.Key = ConsoleKey.Enter) then
                Console.WriteLine()
                pwd
            else

                let newPwd =
                    if (key.Key = ConsoleKey.Backspace && pwd.Length > 0) then
                        Console.Write("\b \b")
                        pwd.Substring(0, pwd.Length - 1)
                    else
                        Console.Write("*")
                        pwd + key.KeyChar.ToString()

                ConsoleReadPasswordLineInternal(newPwd)

        ConsoleReadPasswordLineInternal(String.Empty)

#if LEGACY_FRAMEWORK
    let GetConfigValueFromAppConfig(configKey: string, appConfig: FileInfo) =
        if not(appConfig.Exists) then
            raise
            <| FileNotFoundException(
                "config file not found",
                appConfig.FullName
            )

        let fileMap = ExeConfigurationFileMap()
        fileMap.ExeConfigFilename <- appConfig.FullName

        let config =
            ConfigurationManager.OpenMappedExeConfiguration(
                fileMap,
                ConfigurationUserLevel.None
            )

        config.AppSettings.Settings.[configKey].Value

    let GetConfigValue(configKey: string) =
        let appConfigPath =
            Path.Combine(Directory.GetCurrentDirectory(), "app.config")

        GetConfigValueFromAppConfig(configKey, FileInfo(appConfigPath))
#endif

    let IsRunningInGitLab() : bool =
        let gitlabUserEmail =
            Environment.GetEnvironmentVariable("GITLAB_USER_EMAIL")

        not(String.IsNullOrEmpty(gitlabUserEmail))

    let GetCurrentVersion(dir: DirectoryInfo) : Version =
        let defaultAssemblyVersionFileName = "AssemblyInfo.fs"

        let assemblyVersionFsFiles =
            (Directory.EnumerateFiles(
                dir.FullName,
                defaultAssemblyVersionFileName,
                SearchOption.AllDirectories
            ))

        let assemblyVersionFsFile =
            if assemblyVersionFsFiles.Count() = 1 then
                assemblyVersionFsFiles.Single()
            else
                (Directory.EnumerateFiles(
                    dir.FullName,
                    "Common" + defaultAssemblyVersionFileName,
                    SearchOption.AllDirectories
                ))
                    .SingleOrDefault()

        if assemblyVersionFsFile = null then
            Console.Error.WriteLine(
                "Canonical AssemblyInfo not found in any subfolder (or found too many), cannot extract version number"
            )

            Environment.Exit 1

        let assemblyVersionAttribute = "AssemblyVersion"

        let lineContainingVersionNumber =
            File
                .ReadLines(assemblyVersionFsFile)
                .SingleOrDefault(fun line ->
                    (not(line.Trim().StartsWith("//")))
                    && line.Contains(assemblyVersionAttribute)
                )

        if lineContainingVersionNumber = null then
            Console.Error.WriteLine(
                sprintf
                    "%s attribute not found in %s (or found too many), cannot extract version number"
                    assemblyVersionAttribute
                    assemblyVersionFsFile
            )

            Environment.Exit 1

        let versionNumberStartPosInLine =
            lineContainingVersionNumber.IndexOf("\"")

        if versionNumberStartPosInLine = -1 then
            Console.Error.WriteLine
                "Format unexpected in version string (expecting a starting double quote), cannot extract version number"

            Environment.Exit 1

        let versionNumberEndPosInLine =
            lineContainingVersionNumber.IndexOf(
                "\"",
                versionNumberStartPosInLine + 1
            )

        if versionNumberEndPosInLine = -1 then
            Console.Error.WriteLine
                "Format unexpected in version string (expecting an ending double quote), cannot extract version number"

            Environment.Exit 1

        let version =
            lineContainingVersionNumber.Substring(
                versionNumberStartPosInLine + 1,
                versionNumberEndPosInLine - versionNumberStartPosInLine - 1
            )

        Version(version)


    let SqlReadOnlyGroupName = "sqlreadonly"

    let rec GatherOrGetDefaultPrefix
        (
            args: seq<string>,
            previousIsPrefixArg: bool,
            prefixSet: Option<string>
        ) : string =
        let GatherPrefix(newPrefix: string) : Option<string> =
            match prefixSet with
            | None -> Some newPrefix
            | _ -> failwith("prefix argument duplicated")

        let prefixArgWithEquals = "--prefix="

        match Seq.tryHead args with
        | None ->
            match prefixSet with
            | None ->
                match GuessPlatform() with
                | Platform.Windows ->
                    Environment.GetFolderPath
                        Environment.SpecialFolder.ProgramFiles
                | _ -> "/usr/local"
            | Some prefix -> prefix
        | Some head ->
            let tail = Seq.tail args

            if previousIsPrefixArg then
                GatherOrGetDefaultPrefix(tail, false, GatherPrefix head)
            elif head = "--prefix" then
                GatherOrGetDefaultPrefix(tail, true, prefixSet)
            elif head.StartsWith prefixArgWithEquals then
                GatherOrGetDefaultPrefix(
                    tail,
                    false,
                    GatherPrefix(head.Substring prefixArgWithEquals.Length)
                )
            else
                failwithf "argument not recognized: %s" head

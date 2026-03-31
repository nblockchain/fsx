#!/usr/bin/env dotnet fsi

open System
open System.IO
open System.Diagnostics

/// Validates that a path exists and is a directory
let validatePath (path: string) : DirectoryInfo =
    if not (Directory.Exists(path)) then
        eprintfn "Error: Path '%s' does not exist." path
        exit 1
    let dirInfo = DirectoryInfo(path)
    if not dirInfo.Exists then
        eprintfn "Error: Path '%s' is not a valid directory." path
        exit 1
    dirInfo

/// Gets all file details (name, size) and hidden file count for a directory
let getFileDetails (dirInfo: DirectoryInfo) : Map<string, int64> * int =
    let files = dirInfo.GetFiles("*", SearchOption.AllDirectories)
    let fileMap = files |> Array.map (fun f -> f.Name, f.Length) |> Map.ofArray
    let hiddenCount = files |> Array.filter (fun f -> f.Name.StartsWith(".")) |> Array.length
    (fileMap, hiddenCount)

/// Computes MD5 hash for a file using md5sum command
let computeMd5sum (filePath: string) : string =
    try
        let psi = ProcessStartInfo()
        psi.FileName <- "md5sum"
        psi.Arguments <- sprintf "\"%s\"" filePath
        psi.RedirectStandardOutput <- true
        psi.RedirectStandardError <- true
        psi.UseShellExecute <- false
        psi.CreateNoWindow <- true
        
        use proc = Process.Start(psi)
        let output = proc.StandardOutput.ReadToEnd()
        proc.WaitForExit()
        
        if proc.ExitCode <> 0 then
            let error = proc.StandardError.ReadToEnd()
            failwithf "md5sum failed for '%s': %s" filePath error
        
        // md5sum output format: "hash  filename"
        output.Split(' ').[0].Trim()
    with
    | ex -> failwithf "Error computing MD5 for '%s': %s" filePath ex.Message

/// Parse command line arguments
let parseArgs (args: string[]) =
    let mutable folder1 = ""
    let mutable folder2 = ""
    let mutable isParallel = true  // Default is parallel processing
    
    let rec parse i =
        if i >= args.Length then ()
        else
            match args.[i] with
            | "--non-parallel" ->
                isParallel <- false
                parse (i + 1)
            | arg when arg.StartsWith("-") ->
                eprintfn "Error: Unknown option '%s'" arg
                exit 1
            | arg when folder1 = "" -> folder1 <- arg; parse (i + 1)
            | arg when folder2 = "" -> folder2 <- arg; parse (i + 1)
            | _ ->
                eprintfn "Error: Unexpected argument '%s'" args.[i]
                exit 1
    
    parse 0
    
    if folder1 = "" || folder2 = "" then
        None
    else
        Some(folder1, folder2, isParallel)

/// Main comparison logic
let compareFolders (dir1: DirectoryInfo) (dir2: DirectoryInfo) (isParallel: bool) =
    printfn "Comparing folders:"
    printfn "  Folder 1: %s" dir1.FullName
    printfn "  Folder 2: %s" dir2.FullName
    printfn ""
    
    // Get file details
    let (files1, hidden1) = getFileDetails dir1
    let (files2, hidden2) = getFileDetails dir2
    
    // Check 1: Compare number of files
    let count1 = Map.count files1
    let count2 = Map.count files2
    
    if count1 <> count2 then
        let visible1 = count1 - hidden1
        let visible2 = count2 - hidden2
        eprintfn "Error: Number of files differ."
        eprintfn "  Folder 1 has %d files (%d visible, %d hidden)" count1 visible1 hidden1
        eprintfn "  Folder 2 has %d files (%d visible, %d hidden)" count2 visible2 hidden2
        exit 1
    
    printfn "✓ Number of files matches: %d" count1
    
    // Check 2: Compare file names
    let names1 = Set.ofList (Map.keys files1 |> Seq.toList)
    let names2 = Set.ofList (Map.keys files2 |> Seq.toList)
    
    let onlyIn1 = Set.difference names1 names2
    let onlyIn2 = Set.difference names2 names1
    
    if not (Set.isEmpty onlyIn1) then
        eprintfn "Error: Files only in folder 1: %A" onlyIn1
        exit 1
    
    if not (Set.isEmpty onlyIn2) then
        eprintfn "Error: Files only in folder 2: %A" onlyIn2
        exit 1
    
    printfn "✓ File names match"
    
    // Check 3: Compare file sizes
    let sizeDifferences = 
        files1
        |> Map.filter (fun name size1 -> 
            let size2 = Map.find name files2
            size1 <> size2)
    
    if not (Map.isEmpty sizeDifferences) then
        eprintfn "Error: File sizes differ for the following files:"
        sizeDifferences
        |> Map.iter (fun name size1 ->
            let size2 = Map.find name files2
            eprintfn "  %s: %d bytes (folder 1) vs %d bytes (folder 2)" name size1 size2)
        exit 1
    
    printfn "✓ File sizes match"
    
    // Check 4: Compare MD5 checksums
    // Prepare file pairs for comparison
    let filePairs =
        files1
        |> Map.toArray
        |> Array.map (fun (name, _) ->
            let file1Path = Path.Combine(dir1.FullName, name)
            let file2Path = Path.Combine(dir2.FullName, name)
            (name, file1Path, file2Path))
    
    let totalFiles = Map.count files1
    let mutable completedFiles = 0
    
    // Progress update function
    let updateProgress () =
        completedFiles <- completedFiles + 1
        let pct = int (float completedFiles / float totalFiles * 100.0)
        let barWidth = 30
        let filled = int (float completedFiles / float totalFiles * float barWidth)
        let bar = String.replicate filled "=" + String.replicate (barWidth - filled) "-"
        printf "\r  Progress: [%s] %d/%d (%d%%)" bar completedFiles totalFiles pct
        if completedFiles = totalFiles then
            printfn ""
    
    let filesWithDiffs =
        if isParallel then
            printfn "Computing MD5 checksums in parallel..."
            
            // Process in batches of 20 using Async.Parallel
            let batchSize = 20
            let results = ResizeArray<_>()
            
            filePairs
            |> Array.chunkBySize batchSize
            |> Array.iter (fun batch ->
                let asyncWorkItems =
                    batch
                    |> Array.map (fun (name, path1, path2) ->
                        async {
                            let hash1 = computeMd5sum path1
                            let hash2 = computeMd5sum path2
                            return (name, hash1, hash2)
                        })
                
                let batchResults =
                    asyncWorkItems
                    |> Async.Parallel
                    |> Async.RunSynchronously
                
                batchResults |> Array.iter (fun r ->
                    updateProgress ()
                    results.Add(r))
            )
            
            results.ToArray() |> Array.filter (fun (_, hash1, hash2) -> hash1 <> hash2)
        else
            printfn "Computing MD5 checksums (sequential)..."
            
            filePairs
            |> Array.map (fun (name, path1, path2) ->
                let hash1 = computeMd5sum path1
                let hash2 = computeMd5sum path2
                updateProgress ()
                (name, hash1, hash2))
            |> Array.filter (fun (_, hash1, hash2) -> hash1 <> hash2)
    
    if not (Array.isEmpty filesWithDiffs) then
        eprintfn "Error: MD5 checksums differ for the following files:"
        filesWithDiffs
        |> Array.iter (fun (name, hash1, hash2) ->
            eprintfn "  %s:" name
            eprintfn "    Folder 1: %s" hash1
            eprintfn "    Folder 2: %s" hash2)
        exit 1
    
    printfn "✓ MD5 checksums match for all files"
    printfn ""
    printfn "SUCCESS: All files are identical!"

/// Entry point
let main (args: string[]) =
    match parseArgs args with
    | None ->
        eprintfn "Usage: dir-diff.fsx <folder1> <folder2> [--non-parallel]"
        eprintfn ""
        eprintfn "Compares two folders and checks if their contents are identical."
        eprintfn ""
        eprintfn "Arguments:"
        eprintfn "  folder1         Path to the first folder"
        eprintfn "  folder2         Path to the second folder"
        eprintfn "  --non-parallel  Disable parallel processing (default: parallel)"
        exit 1
    | Some(folder1Path, folder2Path, isParallel) ->
        printfn "Validating paths..."
        let dir1 = validatePath folder1Path
        let dir2 = validatePath folder2Path
        
        compareFolders dir1 dir2 isParallel
        
        0  // Success exit code

#if INTERACTIVE
main fsi.CommandLineArgs.[1..]
#else
main (fsi.CommandLineArgs |> Array.tail)
#endif

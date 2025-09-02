namespace Fsdk

open System
open System.Linq

open Process
open Misc

module Git =

    let private gitCommand = "git"

    let rec private GetBranchFromGitBranch(outchunks: list<string>) =
        match outchunks with
        | [] ->
            failwith
                "current branch not found, unexpected output from `git branch`"
        | head :: tail ->
            if (head.StartsWith("*")) then
                let branchName = head.Substring("* ".Length)
                branchName
            else
                GetBranchFromGitBranch(tail)

    let private IsGitInstalled() : bool =
        let gitCheckCommand =
            match Misc.GuessPlatform() with
            | Misc.Platform.Windows ->
                {
                    Command = "git"
                    Arguments = "--version"
                }
            | _ ->
                {
                    Command = "which"
                    Arguments = "git"
                }

        match Process.Execute(gitCheckCommand, Echo.Off).Result with
        | Success _ -> true
        | Error _ -> false
        | WarningsOrAmbiguous output ->
            output.PrintToConsole()
            Console.WriteLine()
            Console.Out.Flush()
            Console.Error.Flush()
            failwith "Unexpected 'git' output ^ (with warnings?)"

    let private CheckGitIsInstalled() : unit =
        if not(IsGitInstalled()) then
            Console.Error.WriteLine "Could not continue, install 'git' first"
            Environment.Exit 1

    let GetCurrentBranch() =
        CheckGitIsInstalled()

        let gitBranch =
            Process.Execute(
                {
                    Command = gitCommand
                    Arguments = "branch"
                },
                Echo.Off
            )

        let output = gitBranch.UnwrapDefault()
        let branchesOutput = Misc.CrossPlatformStringSplitInLines output

        GetBranchFromGitBranch branchesOutput

    let GetLastCommit() =
        CheckGitIsInstalled()

        let gitLogCmd =
            {
                Command = gitCommand
                Arguments =
                    "log --no-color --first-parent -n1 --pretty=format:%h"
            }

        let gitLastCommit = Process.Execute(gitLogCmd, Echo.Off)
        let output = gitLastCommit.UnwrapDefault()

        let lines = Misc.CrossPlatformStringSplitInLines output

        if (lines.Length <> 1) then
            failwith "Unexpected git output for special git log command"

        lines.[0]

    let private random = Random()

    let private GenerateRandomShortNameWithLettersButNoNumbers() : string =
        let chars = "abcdefghijklmnopqrstuvwxyz"

        let randomCharArray =
            Enumerable
                .Repeat(chars, 8)
                .Select(fun str -> str.[random.Next(str.Length)])
                .ToArray()

        String(randomCharArray)

    let private AddRemote (remoteName: string) (remoteUrl: string) =
        let gitRemoteAdd =
            {
                Command = gitCommand
                Arguments = sprintf "remote add %s %s" remoteName remoteUrl
            }

        Process
            .Execute(gitRemoteAdd, Echo.Off)
            .UnwrapDefault()
        |> ignore<string>

    let private RemoveRemote(remoteName: string) =
        let gitRemoteRemove =
            {
                Command = gitCommand
                Arguments = sprintf "remote remove %s" remoteName
            }

        Process
            .Execute(gitRemoteRemove, Echo.Off)
            .UnwrapDefault()
        |> ignore<string>

    let private GetRemotesInternal() =
        let gitShowRemotes =
            {
                Command = gitCommand
                Arguments = "remote -v"
            }

        Process
            .Execute(gitShowRemotes, Echo.Off)
            .UnwrapDefault()

    let CheckRemotes() =
        let gitRemoteVerbose =
            {
                Command = gitCommand
                Arguments = "remote --verbose"
            }

        let proc = Process.Execute(gitRemoteVerbose, Echo.Off)
        let map = proc.UnwrapDefault() |> Misc.TsvParse

        let removedLastAction =
            Map.map
                (fun (_key: string) (value: string) -> (value.Split(' ').[0]))
                map

        removedLastAction

    let private FetchAll() =
        let gitFetchAll =
            {
                Command = gitCommand
                Arguments = "fetch --all"
            }

        Process
            .Execute(gitFetchAll, Echo.Off)
            .UnwrapDefault()
        |> ignore<string>

    let GetRemotes() =
        let remoteLines = GetRemotesInternal() |> Misc.TsvParse

        seq {
            for KeyValue(remoteName, remoteUrl) in remoteLines do
                yield (remoteName, remoteUrl)
        }

    let private GetNumberOfCommitsBehindAndAheadFromRemoteBranch
        (repoUrl: string)
        (branchName: string)
        : int * int =
        CheckGitIsInstalled()

        let lastCommit = GetLastCommit()
        let remotes = GetRemotes()

        let maybeRemoteFound =
            Seq.tryFind
                (fun (_, remoteUrl: string) -> remoteUrl.Contains repoUrl)
                remotes

        let remote, cleanRemoteLater =
            match maybeRemoteFound with
            | Some(remoteName, _) -> remoteName, false
            | None ->
                let randomNameForRemoteToBeDeletedLater =
                    GenerateRandomShortNameWithLettersButNoNumbers()

                AddRemote randomNameForRemoteToBeDeletedLater repoUrl
                FetchAll()
                randomNameForRemoteToBeDeletedLater, true

        let gitRevListCmd =
            {
                Command = gitCommand
                Arguments =
                    sprintf
                        "rev-list --left-right --count %s/%s...%s"
                        remote
                        branchName
                        lastCommit
            }

        let gitCommitDivergence = Process.Execute(gitRevListCmd, Echo.Off)
        let output = gitCommitDivergence.UnwrapDefault()

        let numbers =
            output.Split([| "\t" |], StringSplitOptions.RemoveEmptyEntries)

        let expectedNumberOfNumbers = 2

        if (numbers.Length <> expectedNumberOfNumbers) then
            failwith(
                sprintf
                    "Unexpected git output for special `git rev-list` command, got %d numbers instead of %d"
                    numbers.Length
                    expectedNumberOfNumbers
            )

        let behind = Int32.Parse(numbers.[0])
        let ahead = Int32.Parse(numbers.[1])

        if cleanRemoteLater then
            RemoveRemote remote

        behind, ahead

    let GetNumberOfCommitsAhead repo branch : int =
        GetNumberOfCommitsBehindAndAheadFromRemoteBranch repo branch |> snd

    let GetNumberOfCommitsBehind repo branch : int =
        GetNumberOfCommitsBehindAndAheadFromRemoteBranch repo branch |> fst

    // 0 == last commit, 1 == second to last, and so on...
    let GetCommitMessageOfLastCommitNumber(number: int) : string =
        if (number < 0) then
            failwith "Expected number param to be non-negative"

        CheckGitIsInstalled()

        let gitLogCmd =
            {
                Command = gitCommand
                Arguments =
                    String.Format(
                        "log --skip={0} -1 --pretty=format:%b",
                        number
                    )
            }

        let gitLastNCommit = Process.Execute(gitLogCmd, Echo.Off)
        gitLastNCommit.UnwrapDefault()

    let GetCommitMessagesOfCommitsInThisBranchNotPresentInRemoteBranch
        repo
        branch
        : seq<string> =
        seq {
            for i = 0 to (GetNumberOfCommitsAhead repo branch) - 1 do
                yield GetCommitMessageOfLastCommitNumber i
        }

    let GetRepoInfo() =
        if not(IsGitInstalled()) then
            String.Empty
        else
            let gitLog =
                Process.Execute(
                    {
                        Command = "git"
                        Arguments = "log --oneline"
                    },
                    Echo.Off
                )

            match gitLog.Result with
            | ProcessResultState.Error _ -> String.Empty
            | ProcessResultState.WarningsOrAmbiguous output ->
                output.PrintToConsole()
                Console.WriteLine()
                Console.Out.Flush()
                Console.Error.Flush()

                failwith
                    "Unexpected git behaviour, as `git log` succeeded with warnings? ^"
            | ProcessResultState.Success _ ->
                let branch = GetCurrentBranch()

                let gitLogCmd =
                    {
                        Command = "git"
                        Arguments =
                            "log --no-color --first-parent -n1 --pretty=format:%h"
                    }

                let gitLastCommit = Process.Execute(gitLogCmd, Echo.Off)

                match gitLastCommit.Result with
                | ProcessResultState.Error(_, output) ->
                    output.PrintToConsole()
                    Console.WriteLine()
                    Console.Out.Flush()
                    Console.Error.Flush()

                    failwith
                        "Unexpected git behaviour, as `git log` succeeded before but not now ^"
                | ProcessResultState.WarningsOrAmbiguous output ->
                    output.PrintToConsole()
                    Console.WriteLine()
                    Console.Out.Flush()
                    Console.Error.Flush()

                    failwith
                        "Unexpected git behaviour, as `git log` succeeded before but now has warnings? ^"
                | ProcessResultState.Success output ->
                    let lines = Misc.CrossPlatformStringSplitInLines output

                    if lines.Length <> 1 then
                        failwith
                            "Unexpected git output for special git log command"
                    else
                        let lastCommitSingleOutput = lines.[0]
                        sprintf "(%s/%s)" branch lastCommitSingleOutput

    let GetTags() =
        let tags =
            Process
                .Execute(
                    {
                        Command = "git"
                        Arguments = "tag"
                    },
                    Echo.All
                )
                .UnwrapDefault()

        let tagsSplitted =
            tags.Split(
                [| "\r\n"; "\n" |],
                StringSplitOptions.RemoveEmptyEntries
            )

        tagsSplitted

    let DoesTagExist(tagName: string) =
        GetTags() |> Seq.contains tagName

    let CreateTag(tagName: string) =
        Process
            .Execute(
                {
                    Command = "git"
                    Arguments = (sprintf "tag %s" tagName)
                },
                Echo.All
            )
            .UnwrapDefault()
        |> ignore<string>

        let processResultRemote =
            Process.Execute(
                {
                    Command = "git"
                    Arguments = "push --tags"
                },
                Echo.All
            )

        let _remoteResultRemote =
            match processResultRemote.Result with
            | ProcessResultState.Error(_exitCode, output) ->
                failwith(
                    sprintf
                        "pushing tags finished with an error: %s"
                        output.StdErr
                )
            | _ -> ()

        ()

    let CreateTagWithForce(tagName: string) =
        Process
            .Execute(
                {
                    Command = "git"
                    Arguments = sprintf "tag %s --force" tagName
                },
                Echo.All
            )
            .UnwrapDefault()
        |> ignore<string>

        let processResultRemote =
            Process.Execute(
                {
                    Command = "git"
                    Arguments =
                        sprintf "push origin \"refs/tags/%s\" --force" tagName
                },
                Echo.All
            )

        match processResultRemote.Result with
        | ProcessResultState.Error(_exitCode, output) ->
            failwithf "pushing tag finished with an error: %s" output.StdErr
        | _ -> ()

    let DeleteTag tagName =
        Process
            .Execute(
                {
                    Command = "git"
                    Arguments = (sprintf "tag --delete %s" tagName)
                },
                Echo.All
            )
            .UnwrapDefault()
        |> ignore<string>

        let processResultRemote =
            Process.Execute(
                {
                    Command = "git"
                    Arguments = (sprintf "push --delete origin %s" tagName)
                },
                Echo.All
            )

        let _processResultRemote =
            match processResultRemote.Result with
            | ProcessResultState.Error(_exitCode, output) ->
                failwith(
                    sprintf
                        "deleting remote tag finished with an error: %s"
                        output.StdErr
                )
            | _ -> ()

        ()

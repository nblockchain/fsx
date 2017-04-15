
namespace FSX.Infrastructure

open System
open System.Text
open System.Threading
open System.Diagnostics

type OutChunk = StdOut of string | StdErr of string
type OutputBuffer = list<OutChunk>
type ProcessResult = { ExitCode: int; Output: OutputBuffer }

module Process =

    let rec PrintToScreen (outputBuffer: OutputBuffer) =
        match outputBuffer with
        | [] -> ()
        | head::tail ->
            match head with
            | StdOut(out) ->
                Console.WriteLine(out)
                Console.Out.Flush()
            | StdErr(err) ->
                Console.Error.WriteLine(err)
                Console.Error.Flush()
            PrintToScreen(tail)

    let Execute (commandWithArguments: string, echo: bool, hidden: bool)
        : ProcessResult =

        // I know, this shit below is mutable, but it's a consequence of dealing with .NET's Process class' events
        let outputBuffer = new System.Collections.Generic.List<OutChunk>()
        let outputBufferLock = new Object()

        use outWaitHandle = new AutoResetEvent(false)
        use errWaitHandle = new AutoResetEvent(false)

        if (echo) then
            Console.WriteLine(commandWithArguments)

        let firstSpaceAt = commandWithArguments.IndexOf(" ")
        let (command, args) =
            if (firstSpaceAt >= 0) then
                (commandWithArguments.Substring(0, firstSpaceAt), commandWithArguments.Substring(firstSpaceAt + 1))
            else
                (commandWithArguments, String.Empty)

        let startInfo = new ProcessStartInfo(command, args)
        startInfo.UseShellExecute <- false
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        use proc = new System.Diagnostics.Process()
        proc.StartInfo <- startInfo

        let outReceived (e: DataReceivedEventArgs): unit =
            if (e.Data = null) then
                outWaitHandle.Set() |> ignore
            else
                if not (hidden) then
                    Console.WriteLine(e.Data)
                    Console.Out.Flush()
                lock outputBufferLock (fun _ -> outputBuffer.Add(OutChunk.StdOut(e.Data)))

        let errReceived (e: DataReceivedEventArgs): unit =
            if (e.Data = null) then
                errWaitHandle.Set() |> ignore
            else
                if not (hidden) then
                    Console.Error.WriteLine(e.Data)
                    Console.Error.Flush()
                lock outputBufferLock (fun _ -> outputBuffer.Add(OutChunk.StdErr(e.Data)))

        proc.OutputDataReceived.Add outReceived
        proc.ErrorDataReceived.Add errReceived

        proc.Start() |> ignore

        let exitCode =
            try
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()

                proc.WaitForExit()
                proc.ExitCode

            finally
                outWaitHandle.WaitOne() |> ignore
                errWaitHandle.WaitOne() |> ignore

        { ExitCode = exitCode; Output = List.ofSeq(outputBuffer) }


module Util =

    let rec private FsxArgumentsInternal(args: string list, fsxFileFound: bool) =
        match args with
        | [] -> []
        | head::tail ->
            match fsxFileFound with
            | false ->
                if (head.EndsWith(".fsx") || //normal FSI way
                    // below for #!/usr/bin/fsx shebang
                    head.EndsWith("fsx.exe")) then
                    FsxArgumentsInternal(tail, true)
                else
                    FsxArgumentsInternal(tail, false)
            | true ->
                if (head.Equals("--")) then
                    tail
                else
                    args

    let FsxArguments() =
        let cmdLineArgs = Environment.GetCommandLineArgs()
        FsxArgumentsInternal((List.ofSeq(cmdLineArgs)), false)


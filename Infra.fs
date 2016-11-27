
namespace FSX.Infrastructure

open System
open System.Text
open System.Threading
open System.Diagnostics

module Process =
    let Execute (commandWithArguments: string, echo: bool, hidden: bool)
        : int * string * string =

        let outBuilder = new StringBuilder()
        let errBuilder = new StringBuilder()

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
                outBuilder.AppendLine(e.Data) |> ignore

        let errReceived (e: DataReceivedEventArgs): unit =
            if (e.Data = null) then
                errWaitHandle.Set() |> ignore
            else
                if not (hidden) then
                    Console.Error.WriteLine(e.Data)
                errBuilder.AppendLine(e.Data) |> ignore

        proc.OutputDataReceived.Add outReceived
        proc.ErrorDataReceived.Add errReceived

        let exitCode =
            try
                proc.Start() |> ignore
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()

                proc.WaitForExit()
                proc.ExitCode

            finally
                outWaitHandle.WaitOne() |> ignore
                errWaitHandle.WaitOne() |> ignore

        (exitCode, outBuilder.ToString(), errBuilder.ToString())


module Util =

    let rec private FsxArgumentsInternal(args: string list, fsxFileFound: bool) =
        match args with
        | [] -> []
        | head::tail ->
            match fsxFileFound with
            | false ->
                if (head.EndsWith(".fsx")) then
                    FsxArgumentsInternal(tail, true)
                else
                    FsxArgumentsInternal(tail, false)
            | true ->
                if (head.Equals("--")) then
                    tail
                else
                    args

    let FsxArguments() =
        FsxArgumentsInternal((List.ofSeq(Environment.GetCommandLineArgs())), false)


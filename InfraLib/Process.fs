namespace FSX.Infrastructure

open System
open System.IO
open System.Diagnostics
open System.Threading
open System.Linq
open System.Text

module Process =

    // https://stackoverflow.com/a/961904/544947
    type internal QueuedLock() =
        let innerLock = Object()
        let ticketsCount = ref 0
        let ticketToRide = ref 1

        member __.Enter() =
            let myTicket = Interlocked.Increment ticketsCount
            Monitor.Enter innerLock

            while myTicket <> Volatile.Read ticketToRide do
                Monitor.Wait innerLock |> ignore

        member __.Exit() =
            Interlocked.Increment ticketToRide |> ignore
            Monitor.PulseAll innerLock
            Monitor.Exit innerLock

    type Standard =
        | Output
        | Error

        override self.ToString() =
            sprintf "%A" self

    type OutputChunk =
        {
            OutputType: Standard
            Chunk: StringBuilder
        }

    type Echo =
        | All
        | OutputOnly
        | Off

    type OutputBuffer(buffer: list<OutputChunk>) =

        //NOTE both Filter() and Print() process tail before head
        // because of the way the buffer-aggregation is implemented in
        // Execute()'s ReadIteration()

        let rec Filter
            (
                subBuffer: list<OutputChunk>,
                outputType: Option<Standard>
            ) =
            match subBuffer with
            | [] -> new StringBuilder()
            | head :: tail ->
                let filteredTail = Filter(tail, outputType)

                if (outputType.IsNone || head.OutputType = outputType.Value) then
                    filteredTail.Append(head.Chunk.ToString())
                else
                    filteredTail

        let rec Print(subBuffer: list<OutputChunk>) : unit =
            match subBuffer with
            | [] -> ()
            | head :: tail ->
                Print(tail)

                match head.OutputType with
                | Standard.Output ->
                    Console.Write(head.Chunk.ToString())
                    Console.Out.Flush()
                | Standard.Error ->
                    Console.Error.Write(head.Chunk.ToString())
                    Console.Error.Flush()

        member this.StdOut = Filter(buffer, Some(Standard.Output)).ToString()

        member this.StdErr = Filter(buffer, Some(Standard.Error)).ToString()

        member this.PrintToConsole() =
            Print(buffer)

        override self.ToString() =
            Filter(buffer, None).ToString()

    type ProcessResultState =
        // exitCode=0, no stdErr
        | Success of output: string

        // exitCode<>0
        | Error of exitCode: int * output: OutputBuffer

        // exitCode=0, some stdErr
        | WarningsOrAmbiguous of output: OutputBuffer

    type ProcessDetails =
        {
            Command: string
            Arguments: string
        }

        override self.ToString() =
            sprintf "Command: %s. Arguments: %s." self.Command self.Arguments


    exception ProcessSucceededWithWarnings of string
    exception ProcessFailed of string

    type ProcessResult =
        {
            Details: ProcessDetails
            Result: ProcessResultState
        }

        member self.Unwrap(errMsg: string) : string =
            match self.Result with
            | Success output -> output
            | Error(_, output) ->
                output.PrintToConsole()
                Console.WriteLine()
                Console.Out.Flush()

                Console.Error.WriteLine errMsg
                raise <| ProcessFailed errMsg
            | WarningsOrAmbiguous output ->
                output.PrintToConsole()
                Console.WriteLine()
                Console.Out.Flush()
                Console.Error.Flush()

                let fullErrMsg = sprintf "%s (with warnings?)" errMsg
                Console.Error.WriteLine fullErrMsg
                raise <| ProcessSucceededWithWarnings fullErrMsg

        member self.UnwrapDefault() : string =
            self.Unwrap(sprintf "Error when running '%s'" self.Details.Command)


    type ProcessCouldNotStart
        (
            procDetails: ProcessDetails,
            innerException: Exception
        ) =
        inherit Exception
            (
                sprintf "Process could not start! %s" (procDetails.ToString()),
                innerException
            )

    let Execute(procDetails: ProcessDetails, echo: Echo) : ProcessResult =

        // I know, this shit below is mutable, but it's a consequence of dealing with .NET's Process class' events?
        let mutable outputBuffer: list<OutputChunk> = []
        let queuedLock = QueuedLock()

        if (echo = Echo.All) then
            Console.WriteLine(
                sprintf "%s %s" procDetails.Command procDetails.Arguments
            )

            Console.Out.Flush()

        let startInfo =
            new ProcessStartInfo(procDetails.Command, procDetails.Arguments)

        startInfo.UseShellExecute <- false
        startInfo.RedirectStandardOutput <- true
        startInfo.RedirectStandardError <- true
        use proc = new System.Diagnostics.Process()
        proc.StartInfo <- startInfo

        let ReadStandard(std: Standard) =

            let print =
                match std with
                | Standard.Output -> Console.Write: char -> unit
                | Standard.Error -> Console.Error.Write

            let flush =
                match std with
                | Standard.Output -> Console.Out.Flush
                | Standard.Error -> Console.Error.Flush

            let outputToReadFrom =
                match std with
                | Standard.Output -> proc.StandardOutput
                | Standard.Error -> proc.StandardError

            let ReadIteration() : bool =
                let append(charToAppend: char) : unit =

                    let newBuilder = StringBuilder(charToAppend.ToString())

                    match outputBuffer with
                    | [] ->
                        let newBlock =
                            match std with
                            | Standard.Output ->
                                {
                                    OutputType = Standard.Output
                                    Chunk = newBuilder
                                }
                            | Standard.Error ->
                                {
                                    OutputType = Standard.Error
                                    Chunk = newBuilder
                                }

                        outputBuffer <- List.singleton newBlock
                    | head :: _tail ->
                        if head.OutputType = std then
                            head.Chunk.Append charToAppend |> ignore
                        else
                            let newBlock =
                                {
                                    OutputType = std
                                    Chunk = newBuilder
                                }

                            outputBuffer <- newBlock :: outputBuffer

                    if not(echo = Echo.Off) then
                        print charToAppend
                        flush()

                // I want to hardcode this to 1 because otherwise the order of the stderr|stdout
                // chunks in the outputbuffer would innecessarily depend on this bufferSize, setting
                // it to 1 makes it slow but then the order is only relying (in theory) on how the
                // streams come and how fast the .NET IO processes them
                let bufferSize = 1

                // 'x' is a dummy value that will get replaced
                let outChar = Array.singleton 'x'
                let uniqueElementIndexInTheSingleCharBuffer = bufferSize - 1

                if not(outChar.Length = bufferSize) then
                    failwith "Buffer Size must equal current buffer size"

                let readTask =
                    outputToReadFrom.ReadAsync(
                        outChar,
                        uniqueElementIndexInTheSingleCharBuffer,
                        bufferSize
                    )

                readTask.Wait()

                if not(readTask.IsCompleted) then
                    failwith "Failed to read"

                let readCount = readTask.Result

                if (readCount > bufferSize) then
                    failwith
                        "StreamReader.Read() should not read more than the bufferSize if we passed the bufferSize as a parameter"

                let singleChar =

                    // meaning readCount < bufferSize (and bufferSize being 1 means readCount=0 or negative)
                    if readCount <> bufferSize then
                        None
                    else
                        outChar.[uniqueElementIndexInTheSingleCharBuffer]
                        |> Some

                match singleChar with
                | None when
                    readCount < 0
                    || (readCount = 0 && outputToReadFrom.EndOfStream)
                    ->
                    false
                | None -> true

                // FIXME: only appending after \n was a previous approach that
                // helped with the test "testProcessConcurrency.fsx" (it was
                // passing in Linux, even if it sometimes failed in macOS):
                //| Some '\n' -> ...

                | Some char ->
                    try
                        queuedLock.Enter()
                        append char
                    finally
                        queuedLock.Exit()

                    true

            // this is a way to do a `do...while` loop in F#...
            while (ReadIteration()) do
                ignore None

        let outReaderThread =
            new Thread(new ThreadStart(fun _ -> ReadStandard(Standard.Output)))

        let errReaderThread =
            new Thread(new ThreadStart(fun _ -> ReadStandard(Standard.Error)))

        try
            proc.Start() |> ignore
        with
        | ex -> raise <| ProcessCouldNotStart(procDetails, ex)

        outReaderThread.Start()
        errReaderThread.Start()
        proc.WaitForExit()
        let exitCode = proc.ExitCode

        outReaderThread.Join()
        errReaderThread.Join()

        let output = OutputBuffer outputBuffer

        match exitCode with
        | 0 when output.StdErr.Length = 0 ->
            {
                Details = procDetails
                Result = ProcessResultState.Success output.StdOut
            }
        | 0 ->
            {
                Details = procDetails
                Result = ProcessResultState.WarningsOrAmbiguous output
            }
        | _ ->
            {
                Details = procDetails
                Result = ProcessResultState.Error(exitCode, output)
            }

    let rec private ExceptionIsOfTypeOrIncludesAnyInnerExceptionOfType
        (
            ex: Exception,
            t: Type
        ) : bool =
        if (ex = null) then
            false
        else if (ex.GetType() = t) then
            true
        else
            ExceptionIsOfTypeOrIncludesAnyInnerExceptionOfType(
                ex.InnerException,
                t
            )

    let rec private CheckIfCommandWorksInShellWithWhich
        (command: string)
        : bool =
        let WhichCommandWorksInShell() : bool =
            let maybeResult =
                try
                    Some(
                        Execute(
                            {
                                Command = "which"
                                Arguments = String.Empty
                            },
                            Echo.Off
                        )
                    )
                with
                | ex when
                    (ExceptionIsOfTypeOrIncludesAnyInnerExceptionOfType(
                        ex,
                        typeof<System.ComponentModel.Win32Exception>
                    ))
                    ->
                    None
                | _ -> reraise()

            match maybeResult with
            | None -> false
            | Some _ -> true

        if not(WhichCommandWorksInShell()) then
            failwith "'which' doesn't work, please install it first"

        let proc =
            Execute(
                {
                    Command = "which"
                    Arguments = command
                },
                Echo.Off
            )

        match proc.Result with
        | ProcessResultState.Error _ -> false
        | ProcessResultState.WarningsOrAmbiguous output ->
            output.PrintToConsole()
            Console.WriteLine()
            Console.Out.Flush()
            Console.Error.Flush()
            failwith "Unexpected 'which' output ^ (with warnings?)"
        | ProcessResultState.Success _ -> true

    let private HasWindowsExecutableExtension(path: string) =
        //FIXME: should do it in a case-insensitive way
        path.EndsWith(".exe")
        || path.EndsWith(".bat")
        || path.EndsWith(".cmd")
        || path.EndsWith(".com")

    let private IsFileInWindowsPath(command: string) =
        let pathEnvVar = Environment.GetEnvironmentVariable("PATH")
        let paths = pathEnvVar.Split(Path.PathSeparator)
        paths.Any(fun path -> File.Exists(Path.Combine(path, command)))

    let CommandWorksInShell(command: string) : bool =
        if (Misc.GuessPlatform() = Misc.Platform.Windows) then
            let exists = File.Exists(command) || IsFileInWindowsPath(command)

            if (exists && HasWindowsExecutableExtension(command)) then
                true
            else
                false
        else
            CheckIfCommandWorksInShellWithWhich(command)

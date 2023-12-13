#!/usr/bin/env fsx

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Linq
open System.Security.Cryptography

let NUMBER_OF_LINES_OF_BUFFER_TO_SHOW_IN_NOTIFICATION = 20

#r "System.Configuration"
open System.Configuration

#load "../Fsdk/Misc.fs"
#load "../Fsdk/Process.fs"
#load "../Fsdk/Unix.fs"
#load "../Fsdk/Git.fs"
#load "../Fsdk/Network.fs"

open Fsdk

open Process

let arguments = Misc.FsxOnlyArguments()
let argCount = arguments.Length

if (argCount = 0) then
    Console.Error.WriteLine(
        "This script expects command (and optionally arguments)"
    )

    Environment.Exit(1)

if (arguments.Any(fun arg -> arg.Contains(">"))
    || arguments.Any(fun arg -> arg.Contains("<"))) then
    Console.Error.WriteLine("This script doesn't support redirections")
    Environment.Exit(2)

let home = Environment.GetEnvironmentVariable("HOME")

if (String.IsNullOrWhiteSpace(home)) then
    failwith("This script assumes that $HOME is defined properly")

let homeLog = Path.Combine(home, "log")

Process
    .Execute(
        {
            Command = "mkdir"
            Arguments = sprintf "-p %s" homeLog
        },
        Echo.Off
    )
    .UnwrapDefault()
|> ignore<string>

let command = arguments.First()
let argumentsOfCommand = String.Join(" ", List.skip 1 arguments)

let commandName = (command.Split('/')).Last()

let now = DateTime.Now.ToString("dddHHmm")

let logForStdOutFileName = sprintf "%s.%s.out.log" commandName now
let logForStdErrFileName = sprintf "%s.%s.err.log" commandName now
let logForGenericStdErrFileName = sprintf "%s.%s.err.log" __SOURCE_FILE__ now

let logForStdOut = Path.Combine(homeLog, logForStdOutFileName)
let logForStdErr = Path.Combine(homeLog, logForStdErrFileName)
let logForGenericStdErr = Path.Combine(homeLog, logForGenericStdErrFileName)

let fullCommand =
    String.Format(
        "{0} {1} 1>{2} 2>{3}",
        command,
        argumentsOfCommand,
        logForStdOut,
        logForStdErr
    )

let proc = Unix.ExecuteBashCommand(fullCommand, Echo.Off)

match proc.Result with
| ProcessResultState.Success _ ->
    let stdErrLog = new FileInfo(logForStdErr)

    if (stdErrLog.Exists && stdErrLog.Length = 0L) then
        stdErrLog.Delete()
| _ ->

#if LEGACY_FRAMEWORK
    let stdErrLines = File.ReadAllLines(logForStdErr)

    let lines =
        if (stdErrLines.Length = 0
            || (stdErrLines.Length = 1
                && String.IsNullOrWhiteSpace(stdErrLines.[0].Trim()))) then
            File.ReadAllLines(logForStdOut)
        else
            stdErrLines

    let skip =
        Math.Max(
            0,
            lines.Length - NUMBER_OF_LINES_OF_BUFFER_TO_SHOW_IN_NOTIFICATION
        )

    let lastLines = (Environment.NewLine, lines.Skip(skip)) |> String.Join

    try
        Network.SlackNotify(
            String.Format(
                "Error running '{0} {1}':{2}{3}",
                command,
                argumentsOfCommand,
                Environment.NewLine,
                lastLines
            )
        )
    with
    | ex ->
        File.WriteAllText(logForGenericStdErr, ex.ToString())

        Network.SlackNotify(
            String.Format(
                "Error trying to notify problem to Slack about '{0} {1}': check {2}",
                command,
                argumentsOfCommand,
                logForGenericStdErr
            )
        )
#else
    ()
#endif

// make 'foo.last.out|err.log' symlinks pointing to last log
let logForLastStdOutName = sprintf "%s.last.out.log" commandName
let logForLastStdErrName = sprintf "%s.last.err.log" commandName
let logForLastStdOutSymLink = Path.Combine(homeLog, logForLastStdOutName)
let logForLastStdErrSymLink = Path.Combine(homeLog, logForLastStdErrName)

Process
    .Execute(
        {
            Command = "ln"
            Arguments = sprintf "-fs %s %s" logForStdOut logForLastStdOutSymLink
        },
        Echo.Off
    )
    .UnwrapDefault()
|> ignore<string>

Process
    .Execute(
        {
            Command = "ln"
            Arguments = sprintf "-fs %s %s" logForStdErr logForLastStdErrSymLink
        },
        Echo.Off
    )
    .UnwrapDefault()
|> ignore<string>

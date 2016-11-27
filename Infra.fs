
namespace Fsx.Infrastructure

open System

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

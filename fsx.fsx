#!/usr/bin/env fsharpi

open System

#load "Infra.fs"
open Fsx.Infrastructure


let PrintUsage () =
    Console.WriteLine("Usage: ./fsx.fsx  [OPTION]... yourscript.fsx")
    Console.WriteLine()
    Console.WriteLine("Options")
    Console.WriteLine("  -c, --compile     Only compile, don't run (ideal for CI build scripts)")

let args = Util.FsxArguments()
if (args.Length = 0) then
    PrintUsage()
    Environment.Exit(1)

System.Console.WriteLine("hello world")
for arg in args do
    System.Console.WriteLine("arg: " + arg)

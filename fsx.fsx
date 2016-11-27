#!/usr/bin/env fsharpi

#load "Infra.fs"
open Fsx.Infrastructure

System.Console.WriteLine("hello world")

let args = Util.FsxArguments()
for arg in args do
    System.Console.WriteLine("arg: " + arg)

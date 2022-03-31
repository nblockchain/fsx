#!/usr/bin/env fsx

open System

let args = fsi.CommandLineArgs

if args.Length < 4 then
    Console.Error.WriteLine(
        sprintf "Failed: expected number of args 4, got %i" args.Length
    )

    Environment.Exit 1

if args.[1] <> "one" then
    Console.Error.WriteLine(
        sprintf "Failed: expected 1st arg to be 'one', got '%s'" args.[1]
    )

    Environment.Exit 2

if args.[2] <> "2" then
    Console.Error.WriteLine(
        sprintf "Failed: expected 2st arg to be '2', got '%s'" args.[2]
    )

    Environment.Exit 3

if args.[3] <> "three" then
    Console.Error.WriteLine(
        sprintf "Failed: expected 3rd arg to be 'three', got '%s'" args.[3]
    )

    Environment.Exit 4

Console.WriteLine "Success"

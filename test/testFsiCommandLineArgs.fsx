#!/usr/bin/env fsx

open System

let args = fsi.CommandLineArgs

if args.Length < 4 then
    Console.Error.WriteLine(
        sprintf "Failed: expected number of args 4, got %i" args.Length
    )

    Environment.Exit 1

let expected = "?,one,2,three"
let got = String.Join(",", args)

if args.[1] <> "one" then
    Console.Error.WriteLine(
        sprintf
            "Failed: different 1st arg; expected '%s', got '%s'"
            expected
            got
    )

    Environment.Exit 2

if args.[2] <> "2" then
    Console.Error.WriteLine(
        sprintf
            "Failed: different 2nd arg; expected '%s', got '%s'"
            expected
            got
    )

    Environment.Exit 3

if args.[3] <> "three" then
    Console.Error.WriteLine(
        sprintf
            "Failed: different 3rd arg; expected '%s', got '%s'"
            expected
            got
    )

    Environment.Exit 4

Console.WriteLine "Success"

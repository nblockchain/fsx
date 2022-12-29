#!/usr/bin/env fsx

open System
open System.IO
open System.Linq

#r "nuget: TickSpec, Version=2.0.1"

let someProcedure() =
    ()

let action: TickSpec.Action = TickSpec.Action someProcedure
Console.WriteLine(action.GetType().FullName)

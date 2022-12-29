#!/usr/bin/env fsx

open System
open System.IO
open System.Linq

#r "nuget: TickSpec"

let someProcedure() =
    ()

let action: TickSpec.Action = TickSpec.Action someProcedure
Console.WriteLine(action.GetType().FullName)

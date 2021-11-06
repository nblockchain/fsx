#!/usr/bin/env fsx

open System
open System.IO
open System.Linq

#r "../packages/Microsoft.Build.16.11.0/lib/net472/Microsoft.Build.dll"
open Microsoft.Build.Construction

let sol = SolutionFile.Parse <| Path.Combine(__SOURCE_DIRECTORY__, "..", "fsx.sln")
for (proj: string) in (sol.ProjectsInOrder.Select(fun p -> p.ProjectName).ToList()) do
    Console.WriteLine proj

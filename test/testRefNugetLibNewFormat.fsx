#!/usr/bin/env fsx

open System
open System.IO
open System.Linq

#r "nuget: Microsoft.Build"
open Microsoft.Build.Construction

let sol =
    SolutionFile.Parse
    <| Path.Combine(__SOURCE_DIRECTORY__, "..", "fsx-legacy.sln")

for (proj: string) in
    (sol
        .ProjectsInOrder
        .Select(fun p -> p.ProjectName)
        .ToList()) do
    Console.WriteLine proj

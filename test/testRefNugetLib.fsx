#!/usr/bin/env fsx

open System
open System.IO
open System.Linq

// with new dotnet (not legacy) we would use #r "nuget: NugetPkgName" so
// this test only applies to legacy
#if LEGACY_FRAMEWORK

#r "../packages/Microsoft.Build.16.11.0/lib/net472/Microsoft.Build.dll"
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

#endif

Console.WriteLine "hello"

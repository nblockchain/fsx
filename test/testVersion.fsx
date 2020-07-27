#!/usr/bin/env fsx

open System
open System.IO

#r "System.Configuration"
#load "../InfraLib/Misc.fs"
open FSX.Infrastructure

module VersionUnitTests =
    let private TestSimpleVersion(): unit =
        Misc.SpecVersion.Parse "1.0" |> ignore
        ()

    let private TestThreeNumberVersion(): unit =
        Misc.SpecVersion.Parse "1.0.0" |> ignore
        ()

    // this passes with .NET's Version but not with Semver's
    let private TestFourNumberVersion(): unit =
        Misc.SpecVersion.Parse "1.0.0.0" |> ignore
        ()

    // this passes with Semver's Version but not with .NET's
    let private TestRcVersion(): unit =
        Misc.SpecVersion.Parse "1.0.0-rc1" |> ignore
        ()

    // this passes with Semver's Version but not with .NET's
    let private TestVerySpecificVersion(): unit =
        Misc.SpecVersion.Parse "0.92.0-date20200610-0309.git-6ab3739" |> ignore
        ()

    let private TestBadVersion v =
        let failed, (ex: Option<Exception>) =
            try
                //to test .NET's BCL's, uncomment this: Version v |> ignore
                Misc.SpecVersion.Parse v |> ignore
                false, None
            with
            | :? FormatException as ex -> true, Some (ex :> Exception)
            | ex -> true, Some ex
        match failed, ex with
        | false, _ ->
            failwithf "Parsing '%s' should have failed" v
        | _, Some ex ->
            match ex with
            | :? FormatException ->
                ()
            | _ ->
                failwithf "Parsing '%s' should have failed with a FormatException but got %s "
                          v (ex.GetType().FullName)
        | _ -> failwith "unreachable"

    let private TestBadVersion1(): unit =
        TestBadVersion "."

    let private TestBadVersion2(): unit =
        TestBadVersion ".."

    let private TestBadVersion3(): unit =
        TestBadVersion "1"

    let private TestBadVersion4(): unit =
        TestBadVersion "1234"

    let private TestBadVersion5(): unit =
        TestBadVersion "1234."
        TestBadVersion ".1234"

        TestBadVersion " .1234"
        TestBadVersion "1234. "

    let private TestToString() =
        let versionString = "1.0.0.0-beta001"
        let v = Misc.SpecVersion.Parse versionString
        if v.ToString() <> versionString then
            failwith "ToString() failed"

    let private TestSimpleVersionComparison(): unit =
        let oldVersion = Misc.SpecVersion.Parse "0.0.1"
        let newVersion = Misc.SpecVersion.Parse "1.0.0"
        if oldVersion >= newVersion then
            failwith "Simple version comparison failed #1"

        if Misc.SpecVersion.Parse "1.0.0" >= Misc.SpecVersion.Parse "1.1.0" then
            failwith "Simple version comparison failed #2"

        if Misc.SpecVersion.Parse "1.1.0" >= Misc.SpecVersion.Parse "1.1.1" then
            failwith "Simple version comparison failed #3"

        if Misc.SpecVersion.Parse "1.1.1.0" >= Misc.SpecVersion.Parse "1.1.1.1" then
            failwith "Simple version comparison failed #4"

        if Misc.SpecVersion.Parse "1.1" >= Misc.SpecVersion.Parse "1.1.1" then
            failwith "Simple version comparison failed #5"

    let private TestNuancedVersionComparison() = // version numbers are not decimal numbers!
        if Misc.SpecVersion.Parse "0.2" >= Misc.SpecVersion.Parse "0.10" then
            failwith "Nuanced version comparison failed #1"

        if Misc.SpecVersion.Parse "0.0.2" >= Misc.SpecVersion.Parse "0.0.10" then
            failwith "Nuanced version comparison failed #2"

        if Misc.SpecVersion.Parse "0.0.0.2" >= Misc.SpecVersion.Parse "0.0.0.10" then
            failwith "Nuanced version comparison failed #3"

    let private TestRcVersionComparison(): unit =
        let oldVersion = Misc.SpecVersion.Parse "1.0.0-rc1"
        let newVersion = Misc.SpecVersion.Parse "1.0.0"
        if oldVersion >= newVersion then
            failwith "RC version comparison failed #1"

        if Misc.SpecVersion.Parse "1.0.0-rc1" >= Misc.SpecVersion.Parse "1.0.0-rc2" then
            failwith "RC version comparison failed #2"

    let private TestVerySpecificVersionComparison(): unit =
        let oldVersion = Misc.SpecVersion.Parse "0.92.0-date20200610-0309.git-6ab3739"
        let newVersion = Misc.SpecVersion.Parse "0.92.0-date20200610-0310.git-6ab3739"
        if oldVersion >= newVersion then
            failwith "Specific version comparison failed #1"
        ()

        let sameVersion1 = Misc.SpecVersion.Parse "0.92.0-date20200610-0309.git-6ab3739"
        let sameVersion2 = Misc.SpecVersion.Parse "0.92.0-date20200610-0309.git-6ab3739"
        if sameVersion1 > sameVersion2 || sameVersion1 < sameVersion2 then
            failwith "Specific version comparison failed #2"
        ()

    let TestTsvParse(): unit =
        try
           TestSimpleVersion()
           TestThreeNumberVersion()
           TestFourNumberVersion()
           TestRcVersion()
           TestVerySpecificVersion()

           TestBadVersion1()
           TestBadVersion2()
           TestBadVersion3()
           TestBadVersion4()
           TestBadVersion5()

           TestToString()

           TestSimpleVersionComparison()
           TestNuancedVersionComparison()
           TestRcVersionComparison()
           TestVerySpecificVersionComparison()
        with
        | ex -> raise <| Exception ("Tests failed", ex)

VersionUnitTests.TestTsvParse()

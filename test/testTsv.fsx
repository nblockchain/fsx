#!/usr/bin/env fsx
open System
open System.IO

#r "System.Configuration"
open System.Configuration

#load "../Fsdk/Misc.fs"
open Fsdk

module TsvUnitTests =
    let private TestSymmetricTsvSimple() : unit =
        let simpleTsv = "A:\t1\nB:\t2"
        let map = Misc.TsvParse simpleTsv

        if not(map.Count = 2) then
            failwithf "Should have count 2 but had %i" map.Count

        if not(map.Item "A:" = "1") then
            failwith "A should map to 1"

    let private TestTsvAsymmetricVertical() : unit =
        let simpleTsv = "A:\t1\nB:\t2\nC:\t3"
        let map = Misc.TsvParse simpleTsv

        if not(map.Count = 3) then
            failwithf "Vertical test: Should have count 3 but had %i" map.Count

        if not(map.Item "C:" = "3") then
            failwith "Vertical test: C should map to 3"

    let private TestTsvAsymmetricHorizontal() : unit =
        let simpleTsv = "A\tB\tC\n1\t2\t3"
        let map = Misc.TsvParse simpleTsv

        if not(map.Count = 3) then
            failwithf
                "Horizontal test: Should have count 3 but had %i"
                map.Count

        if not(map.Item "C" = "3") then
            failwith "Horizontal test: C should map to 3"

    let private TestTsvHoles() : unit =
        let simpleTsv = "A\tB\tC\n1\t\t3"
        let map = Misc.TsvParse simpleTsv

        if not(map.Count = 3) then
            failwithf "Holes test: Should have count 3 but had %i" map.Count

        if not(map.Item "B" = String.Empty) then
            failwith "Holes test: B should map to String.Empty"

    let TestTsvParse() : unit =
        try
            TestSymmetricTsvSimple()
            TestTsvAsymmetricVertical()
            TestTsvAsymmetricHorizontal()
            TestTsvHoles()
        with
        | ex -> raise <| Exception("Tests failed", ex)

TsvUnitTests.TestTsvParse()

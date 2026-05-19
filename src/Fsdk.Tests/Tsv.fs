namespace Fsdk.Tests

open System

open NUnit.Framework

open Fsdk

[<TestFixture>]
type Tsv() =
    [<Test>]
    member __.TestSymmetricTsvSimple() : unit =
        let simpleTsv = "A:\t1\nB:\t2"
        let map = Misc.TsvParse simpleTsv

        Assert.AreEqual(
            map.Count,
            2,
            sprintf "Should have count 2 but had %i" map.Count
        )

        Assert.AreEqual(map.Item "A:", "1", "A should map to 1")

    [<Test>]
    member __.TestTsvAsymmetricVertical() : unit =
        let simpleTsv = "A:\t1\nB:\t2\nC:\t3"
        let map = Misc.TsvParse simpleTsv

        Assert.AreEqual(
            map.Count,
            3,
            sprintf "Vertical test: Should have count 3 but had %i" map.Count
        )

        Assert.AreEqual(map.Item "C:", "3", "Vertical test: C should map to 3")

    [<Test>]
    member __.TestTsvAsymmetricHorizontal() : unit =
        let simpleTsv = "A\tB\tC\n1\t2\t3"
        let map = Misc.TsvParse simpleTsv

        Assert.AreEqual(
            map.Count,
            3,
            sprintf "Horizontal test: Should have count 3 but had %i" map.Count
        )

        Assert.AreEqual(map.Item "C", "3", "Horizontal test: C should map to 3")

    [<Test>]
    member __.TestTsvHoles() : unit =
        let simpleTsv = "A\tB\tC\n1\t\t3"
        let map = Misc.TsvParse simpleTsv

        Assert.AreEqual(
            map.Count,
            3,
            sprintf "Holes test: Should have count 3 but had %i" map.Count
        )

        Assert.AreEqual(
            map.Item "B",
            String.Empty,
            "Holes test: B should map to String.Empty"
        )

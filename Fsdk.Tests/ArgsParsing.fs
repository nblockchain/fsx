namespace Fsdk.Tests

open NUnit.Framework

open Fsdk

[<TestFixture>]
type ArgsParsing() =

    [<Test>]
    member __.``simplest flags usage``() =
        let commandLine = "someProgram --someLongFlag1 -f2".Split(' ')

        let res: Misc.ArgsParsed =
            Misc.ParseArgs commandLine (fun arg -> arg = "someProgram")

        match res with
        | Misc.ArgsParsed.OnlyFlags flags ->
            Assert.That(Seq.length flags, Is.EqualTo 2)
            Assert.That(Seq.item 0 flags, Is.EqualTo "--someLongFlag1")
            Assert.That(Seq.item 1 flags, Is.EqualTo "-f2")
        | _ -> Assert.Fail "res was not ArgsParsing.OnlyFlags subtype"

    [<Test>]
    member __.``pre and post flags``() =
        let commandLine =
            "someProgram --someLongPreFlag1 -f2 someNonFlagArg --someLongPostFlag3 -f4"
                .Split(' ')

        let res = Misc.ParseArgs commandLine (fun arg -> arg = "someProgram")

        match res with
        | Misc.ArgsParsed.BothFlags(preFlags, arg, postFlags) ->
            Assert.That(arg, Is.EqualTo "someNonFlagArg")
            Assert.That(Seq.length preFlags, Is.EqualTo 2)
            Assert.That(Seq.item 0 preFlags, Is.EqualTo "--someLongPreFlag1")
            Assert.That(Seq.item 1 preFlags, Is.EqualTo "-f2")
            Assert.That(Seq.length postFlags, Is.EqualTo 2)
            Assert.That(Seq.item 0 postFlags, Is.EqualTo "--someLongPostFlag3")
            Assert.That(Seq.item 1 postFlags, Is.EqualTo "-f4")
        | _ -> Assert.Fail "res was not ArgsParsing.BothFlags subtype"

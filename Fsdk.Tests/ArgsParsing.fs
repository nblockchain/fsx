namespace Fsdk.Tests

open NUnit.Framework

open Fsdk

[<TestFixture>]
type ArgsParsing() =

    let MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
        (commandLine: string)
        (programNameWhenFiltering: string)
        =
        let commandLineElements = commandLine.Split(' ')

        let resWithProgramNameFiltering =
            Misc.ParseArgs
                commandLineElements
                (Some(fun arg -> arg = programNameWhenFiltering))

        let commandLineElementsWithNoProgram = Seq.skip 1 commandLineElements

        let resWihhoutProgramNameFiltering =
            Misc.ParseArgs commandLineElementsWithNoProgram None

        resWithProgramNameFiltering, resWihhoutProgramNameFiltering

    [<Test>]
    member __.``no args``() =
        let commandLine = "someProgram"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgram"

        let assertRes res =
            match res with
            | Misc.ArgsParsed.NoArgsWhatsoever -> Assert.Pass()
            | _ ->
                Assert.Fail
                    "results were not ArgsParsing.NoArgsWhatsoever subtype"

        assertRes res
        assertRes resNoProgramFilter

    [<Test>]
    member __.``single arg``() =
        let commandLine = "someProgram someArg"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgram"

        let assertRes res =
            match res with
            | Misc.ArgsParsed.ArgsWithoutFlags args ->
                Assert.That(args.Length, Is.EqualTo 1)
                let arg = args.[0]
                Assert.That(arg, Is.EqualTo "someArg")
            | _ ->
                Assert.Fail "res was not ArgsParsing.ArgsWithoutFlags subtype"

        assertRes res
        assertRes resNoProgramFilter

    [<Test>]
    member __.``only args``() =
        let commandLine = "someProgram someArg1 someArg2"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgram"

        let assertRes res =
            match res with
            | Misc.ArgsParsed.ArgsWithoutFlags args ->
                Assert.That(args.Length, Is.EqualTo 2)
                Assert.That(args.[0], Is.EqualTo "someArg1")
                Assert.That(args.[1], Is.EqualTo "someArg2")
            | _ ->
                Assert.Fail "res was not ArgsParsing.ArgsWithoutFlags subtype"

        assertRes res
        assertRes resNoProgramFilter

    [<Test>]
    member __.``simplest flags usage``() =
        let commandLine = "someProgram --someLongFlag1 -f2"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgram"

        let assertRes res =
            match res with
            | Misc.ArgsParsed.OnlyFlags flags ->
                Assert.That(Seq.length flags, Is.EqualTo 2)
                Assert.That(Seq.item 0 flags, Is.EqualTo "--someLongFlag1")
                Assert.That(Seq.item 1 flags, Is.EqualTo "-f2")
            | _ -> Assert.Fail "res was not ArgsParsing.OnlyFlags subtype"

        assertRes res
        assertRes resNoProgramFilter

    [<Test>]
    member __.``pre and post flags``() =
        let commandLine =
            "someProgram --someLongPreFlag1 -f2 someNonFlagArg --someLongPostFlag3 -f4"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgram"

        let assertRes res =
            match res with
            | Misc.ArgsParsed.ArgsWithFlags(preFlags, args, postFlags) ->
                Assert.That(args.Length, Is.EqualTo 1)
                let arg = args.[0]
                Assert.That(arg, Is.EqualTo "someNonFlagArg")
                Assert.That(Seq.length preFlags, Is.EqualTo 2)

                Assert.That(
                    Seq.item 0 preFlags,
                    Is.EqualTo "--someLongPreFlag1"
                )

                Assert.That(Seq.item 1 preFlags, Is.EqualTo "-f2")
                Assert.That(Seq.length postFlags, Is.EqualTo 2)

                Assert.That(
                    Seq.item 0 postFlags,
                    Is.EqualTo "--someLongPostFlag3"
                )

                Assert.That(Seq.item 1 postFlags, Is.EqualTo "-f4")
            | _ -> Assert.Fail "res1 was not ArgsParsing.ArgsWithFlags subtype"

        assertRes res
        assertRes resNoProgramFilter

        let commandLine =
            "someProgram --someLongPreFlag1 -f2 someNonFlagArg1 someNonFlagArg2 --someLongPostFlag3 -f4"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgram"

        let assertRes res =
            match res with
            | Misc.ArgsParsed.ArgsWithFlags(preFlags, args, postFlags) ->
                Assert.That(args.Length, Is.EqualTo 2)
                Assert.That(args.[0], Is.EqualTo "someNonFlagArg1")
                Assert.That(args.[1], Is.EqualTo "someNonFlagArg2")
                Assert.That(Seq.length preFlags, Is.EqualTo 2)

                Assert.That(
                    Seq.item 0 preFlags,
                    Is.EqualTo "--someLongPreFlag1"
                )

                Assert.That(Seq.item 1 preFlags, Is.EqualTo "-f2")
                Assert.That(Seq.length postFlags, Is.EqualTo 2)

                Assert.That(
                    Seq.item 0 postFlags,
                    Is.EqualTo "--someLongPostFlag3"
                )

                Assert.That(Seq.item 1 postFlags, Is.EqualTo "-f4")
            | _ -> Assert.Fail "res2 was not ArgsParsing.ArgsWithFlags subtype"

        assertRes res
        assertRes resNoProgramFilter

        let commandLine =
            "someProgram someNonFlagArg1 someNonFlagArg2 --someLongPostFlag3 -f4"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgram"

        let assertRes res =
            match res with
            | Misc.ArgsParsed.ArgsWithFlags(preFlags, args, postFlags) ->
                Assert.That(args.Length, Is.EqualTo 2)
                Assert.That(args.[0], Is.EqualTo "someNonFlagArg1")
                Assert.That(args.[1], Is.EqualTo "someNonFlagArg2")
                Assert.That(Seq.length preFlags, Is.EqualTo 0)
                Assert.That(Seq.length postFlags, Is.EqualTo 2)

                Assert.That(
                    Seq.item 0 postFlags,
                    Is.EqualTo "--someLongPostFlag3"
                )

                Assert.That(Seq.item 1 postFlags, Is.EqualTo "-f4")
            | _ -> Assert.Fail "res3 was not ArgsParsing.ArgsWithFlags subtype"

        assertRes res
        assertRes resNoProgramFilter

        let commandLine =
            "someProgram --someLongPreFlag1 -f2 someNonFlagArg1 someNonFlagArg2"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgram"

        let assertRes res =
            match res with
            | Misc.ArgsParsed.ArgsWithFlags(preFlags, args, postFlags) ->
                Assert.That(args.Length, Is.EqualTo 2)
                Assert.That(args.[0], Is.EqualTo "someNonFlagArg1")
                Assert.That(args.[1], Is.EqualTo "someNonFlagArg2")
                Assert.That(Seq.length preFlags, Is.EqualTo 2)

                Assert.That(
                    Seq.item 0 preFlags,
                    Is.EqualTo "--someLongPreFlag1"
                )

                Assert.That(Seq.item 1 preFlags, Is.EqualTo "-f2")
                Assert.That(Seq.length postFlags, Is.EqualTo 0)
            | _ -> Assert.Fail "res3 was not ArgsParsing.ArgsWithFlags subtype"

        assertRes res
        assertRes resNoProgramFilter

    [<Test>]
    member __.errors() =
        let commandLine = "someProgramThatDoesNotMatchPredicate"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgramArgThatDoesNotMatchProgramUsedInCommandLine"

        match res with
        | Misc.ArgsParsed.ErrorDetectingProgram -> ()
        | _ ->
            Assert.Fail "res1 was not ArgsParsing.ErrorDetectingProgram subtype"

        match resNoProgramFilter with
        | Misc.ArgsParsed.NoArgsWhatsoever -> ()
        | otherSubType ->
            Assert.Fail
            <| sprintf
                "res1NoProgramFilter was not ArgsParsed.NoArgsWhatsoever subtype but %A"
                otherSubType

        let commandLine = "someProgramThatDoesNotMatchPredicate someArg"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgramArgThatDoesNotMatchProgramUsedInCommandLine"

        match res with
        | Misc.ArgsParsed.ErrorDetectingProgram -> ()
        | _ ->
            Assert.Fail "res2 was not ArgsParsing.ErrorDetectingProgram subtype"

        match resNoProgramFilter with
        | Misc.ArgsParsed.ArgsWithoutFlags args ->
            Assert.That(args.Length, Is.EqualTo 1)
            Assert.That(args.[0], Is.EqualTo "someArg")
        | _ ->
            Assert.Fail
                "res2NoProgramFilter was not ArgsParsed.ArgsWithoutFlags subtype"

        let commandLine = "someProgramThatDoesNotMatchPredicate --someFlag"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgramArgThatDoesNotMatchProgramUsedInCommandLine"

        match res with
        | Misc.ArgsParsed.ErrorDetectingProgram -> ()
        | _ ->
            Assert.Fail "res3 was not ArgsParsing.ErrorDetectingProgram subtype"

        match resNoProgramFilter with
        | Misc.ArgsParsed.OnlyFlags flags ->
            Assert.That(flags.Length, Is.EqualTo 1)
            Assert.That(flags.[0], Is.EqualTo "--someFlag")

        | _ ->
            Assert.Fail
                "res3NoProgramFilter was not ArgsParsed.ArgsWithFlags subtype"

        let commandLine =
            "someProgramThatDoesNotMatchPredicate someArg --someFlag"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgramArgThatDoesNotMatchProgramUsedInCommandLine"

        match res with
        | Misc.ArgsParsed.ErrorDetectingProgram -> ()
        | _ ->
            Assert.Fail "res4 was not ArgsParsing.ErrorDetectingProgram subtype"

        match resNoProgramFilter with
        | Misc.ArgsParsed.ArgsWithFlags(preFlags, args, postFlags) ->
            Assert.That(preFlags.Length, Is.EqualTo 0)
            Assert.That(args.Length, Is.EqualTo 1)
            Assert.That(args.[0], Is.EqualTo "someArg")
            Assert.That(postFlags.Length, Is.EqualTo 1)
            Assert.That(postFlags.[0], Is.EqualTo "--someFlag")
        | _ ->
            Assert.Fail
                "res4NoProgramFilter was not ArgsParsed.ArgsWithFlags subtype"

        let commandLine =
            "someProgramThatDoesNotMatchPredicate --somePreFlag someArg --somePostFlag"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgramArgThatDoesNotMatchProgramUsedInCommandLine"

        match res with
        | Misc.ArgsParsed.ErrorDetectingProgram -> ()
        | _ ->
            Assert.Fail "res5 was not ArgsParsing.ErrorDetectingProgram subtype"

        match resNoProgramFilter with
        | Misc.ArgsParsed.ArgsWithFlags(preFlags, args, postFlags) ->
            Assert.That(preFlags.Length, Is.EqualTo 1)
            Assert.That(preFlags.[0], Is.EqualTo "--somePreFlag")
            Assert.That(args.Length, Is.EqualTo 1)
            Assert.That(args.[0], Is.EqualTo "someArg")
            Assert.That(postFlags.Length, Is.EqualTo 1)
            Assert.That(postFlags.[0], Is.EqualTo "--somePostFlag")
        | otherSubType ->
            Assert.Fail
            <| sprintf
                "res5NoProgramFilter was not ArgsParsed.ArgsWithFlags subtype but %A"
                otherSubType

        let commandLine =
            "someProgram someArg1 --somePreFlag someArg2 --somePostFlag"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgram"

        let assertRes res =
            match res with
            | Misc.ArgsParsed.ErrorDetectingMoreArgsAfterPreFlags -> ()
            | otherSubType ->
                Assert.Fail
                <| sprintf
                    "res was not ArgsParsed.ErrorDetectingMoreArgsAfterPreFlags subtype but %A"
                    otherSubType

        assertRes res
        assertRes resNoProgramFilter

        let commandLine = "someProgram someArg1 --somePreFlag someArg2"

        let res, resNoProgramFilter =
            MakeTestDoubleInvocationWithAndWithoutProgramNameFiltering
                commandLine
                "someProgram"

        assertRes res
        assertRes resNoProgramFilter

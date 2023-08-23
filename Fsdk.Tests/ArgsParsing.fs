namespace Fsdk.Tests

open NUnit.Framework

open Fsdk

[<TestFixture>]
type ArgsParsing() =

    [<Test>]
    member __.``no args``() =
        let commandLine = "someProgram".Split(' ')

        let res = Misc.ParseArgs commandLine (fun arg -> arg = "someProgram")

        match res with
        | Misc.ArgsParsed.NoArgsWhatsoever -> Assert.Pass()
        | _ -> Assert.Fail "res was not ArgsParsing.NoArgsWhatsoever subtype"

    [<Test>]
    member __.``single arg``() =
        let commandLine = "someProgram someArg".Split(' ')

        let res = Misc.ParseArgs commandLine (fun arg -> arg = "someProgram")

        match res with
        | Misc.ArgsParsed.ArgsWithoutFlags args ->
            Assert.That(args.Length, Is.EqualTo 1)
            let arg = args.[0]
            Assert.That(arg, Is.EqualTo "someArg")
        | _ -> Assert.Fail "res was not ArgsParsing.ArgsWithoutFlags subtype"

    [<Test>]
    member __.``only args``() =
        let commandLine = "someProgram someArg1 someArg2".Split(' ')

        let res = Misc.ParseArgs commandLine (fun arg -> arg = "someProgram")

        match res with
        | Misc.ArgsParsed.ArgsWithoutFlags args ->
            Assert.That(args.Length, Is.EqualTo 2)
            Assert.That(args.[0], Is.EqualTo "someArg1")
            Assert.That(args.[1], Is.EqualTo "someArg2")
        | _ -> Assert.Fail "res was not ArgsParsing.ArgsWithoutFlags subtype"

    [<Test>]
    member __.``simplest flags usage``() =
        let commandLine = "someProgram --someLongFlag1 -f2".Split(' ')

        let res = Misc.ParseArgs commandLine (fun arg -> arg = "someProgram")

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
        | Misc.ArgsParsed.ArgsWithFlags(preFlags, args, postFlags) ->
            Assert.That(args.Length, Is.EqualTo 1)
            let arg = args.[0]
            Assert.That(arg, Is.EqualTo "someNonFlagArg")
            Assert.That(Seq.length preFlags, Is.EqualTo 2)
            Assert.That(Seq.item 0 preFlags, Is.EqualTo "--someLongPreFlag1")
            Assert.That(Seq.item 1 preFlags, Is.EqualTo "-f2")
            Assert.That(Seq.length postFlags, Is.EqualTo 2)
            Assert.That(Seq.item 0 postFlags, Is.EqualTo "--someLongPostFlag3")
            Assert.That(Seq.item 1 postFlags, Is.EqualTo "-f4")
        | _ -> Assert.Fail "res1 was not ArgsParsing.ArgsWithFlags subtype"

        let commandLine =
            "someProgram --someLongPreFlag1 -f2 someNonFlagArg1 someNonFlagArg2 --someLongPostFlag3 -f4"
                .Split(' ')

        let res = Misc.ParseArgs commandLine (fun arg -> arg = "someProgram")

        match res with
        | Misc.ArgsParsed.ArgsWithFlags(preFlags, args, postFlags) ->
            Assert.That(args.Length, Is.EqualTo 2)
            Assert.That(args.[0], Is.EqualTo "someNonFlagArg1")
            Assert.That(args.[1], Is.EqualTo "someNonFlagArg2")
            Assert.That(Seq.length preFlags, Is.EqualTo 2)
            Assert.That(Seq.item 0 preFlags, Is.EqualTo "--someLongPreFlag1")
            Assert.That(Seq.item 1 preFlags, Is.EqualTo "-f2")
            Assert.That(Seq.length postFlags, Is.EqualTo 2)
            Assert.That(Seq.item 0 postFlags, Is.EqualTo "--someLongPostFlag3")
            Assert.That(Seq.item 1 postFlags, Is.EqualTo "-f4")
        | _ -> Assert.Fail "res2 was not ArgsParsing.ArgsWithFlags subtype"

        let commandLine =
            "someProgram someNonFlagArg1 someNonFlagArg2 --someLongPostFlag3 -f4"
                .Split(' ')

        let res = Misc.ParseArgs commandLine (fun arg -> arg = "someProgram")

        match res with
        | Misc.ArgsParsed.ArgsWithFlags(preFlags, args, postFlags) ->
            Assert.That(args.Length, Is.EqualTo 2)
            Assert.That(args.[0], Is.EqualTo "someNonFlagArg1")
            Assert.That(args.[1], Is.EqualTo "someNonFlagArg2")
            Assert.That(Seq.length preFlags, Is.EqualTo 0)
            Assert.That(Seq.length postFlags, Is.EqualTo 2)
            Assert.That(Seq.item 0 postFlags, Is.EqualTo "--someLongPostFlag3")
            Assert.That(Seq.item 1 postFlags, Is.EqualTo "-f4")
        | _ -> Assert.Fail "res3 was not ArgsParsing.ArgsWithFlags subtype"


        let commandLine =
            "someProgram --someLongPreFlag1 -f2 someNonFlagArg1 someNonFlagArg2"
                .Split(' ')

        let res = Misc.ParseArgs commandLine (fun arg -> arg = "someProgram")

        match res with
        | Misc.ArgsParsed.ArgsWithFlags(preFlags, args, postFlags) ->
            Assert.That(args.Length, Is.EqualTo 2)
            Assert.That(args.[0], Is.EqualTo "someNonFlagArg1")
            Assert.That(args.[1], Is.EqualTo "someNonFlagArg2")
            Assert.That(Seq.length preFlags, Is.EqualTo 2)
            Assert.That(Seq.item 0 preFlags, Is.EqualTo "--someLongPreFlag1")
            Assert.That(Seq.item 1 preFlags, Is.EqualTo "-f2")
            Assert.That(Seq.length postFlags, Is.EqualTo 0)
        | _ -> Assert.Fail "res3 was not ArgsParsing.ArgsWithFlags subtype"

    [<Test>]
    member __.errors() =
        let commandLine = "someProgramThatDoesNotMatchPredicate".Split(' ')

        let res =
            Misc.ParseArgs
                commandLine
                (fun arg ->
                    arg = "someProgramArgThatDoesNotMatchProgramUsedInCommandLine"
                )

        match res with
        | Misc.ArgsParsed.ErrorDetectingProgram -> ()
        | _ ->
            Assert.Fail "res1 was not ArgsParsing.ErrorDetectingProgram subtype"

        let commandLine =
            "someProgramThatDoesNotMatchPredicate someArg"
                .Split(' ')

        let res =
            Misc.ParseArgs
                commandLine
                (fun arg ->
                    arg = "someProgramArgThatDoesNotMatchProgramUsedInCommandLine"
                )

        match res with
        | Misc.ArgsParsed.ErrorDetectingProgram -> ()
        | _ ->
            Assert.Fail "res2 was not ArgsParsing.ErrorDetectingProgram subtype"

        let commandLine =
            "someProgramThatDoesNotMatchPredicate --someFlag"
                .Split(' ')

        let res =
            Misc.ParseArgs
                commandLine
                (fun arg ->
                    arg = "someProgramArgThatDoesNotMatchProgramUsedInCommandLine"
                )

        match res with
        | Misc.ArgsParsed.ErrorDetectingProgram -> ()
        | _ ->
            Assert.Fail "res3 was not ArgsParsing.ErrorDetectingProgram subtype"

        let commandLine =
            "someProgramThatDoesNotMatchPredicate someArg --someFlag"
                .Split(' ')

        let res =
            Misc.ParseArgs
                commandLine
                (fun arg ->
                    arg = "someProgramArgThatDoesNotMatchProgramUsedInCommandLine"
                )

        match res with
        | Misc.ArgsParsed.ErrorDetectingProgram -> ()
        | _ ->
            Assert.Fail "res4 was not ArgsParsing.ErrorDetectingProgram subtype"


        let commandLine =
            "someProgramThatDoesNotMatchPredicate --somePreFlag someArg --somePostFlag"
                .Split(' ')

        let res =
            Misc.ParseArgs
                commandLine
                (fun arg ->
                    arg = "someProgramArgThatDoesNotMatchProgramUsedInCommandLine"
                )

        match res with
        | Misc.ArgsParsed.ErrorDetectingProgram -> ()
        | _ ->
            Assert.Fail "res5 was not ArgsParsing.ErrorDetectingProgram subtype"

        let commandLine =
            "someProgram someArg1 --somePreFlag someArg2 --somePostFlag"
                .Split(' ')

        let res = Misc.ParseArgs commandLine (fun arg -> arg = "someProgram")

        match res with
        | Misc.ArgsParsed.ErrorDetectingMoreArgsAfterPreFlags -> ()
        | _ ->
            Assert.Fail
                "res6 was not ArgsParsing.ErrorDetectingMoreArgsAfterPreFlags subtype"

        let commandLine =
            "someProgram someArg1 --somePreFlag someArg2"
                .Split(' ')

        let res = Misc.ParseArgs commandLine (fun arg -> arg = "someProgram")

        match res with
        | Misc.ArgsParsed.ErrorDetectingMoreArgsAfterPreFlags -> ()
        | _ ->
            Assert.Fail
                "res7 was not ArgsParsing.ErrorDetectingMoreArgsAfterPreFlags subtype"

namespace Fsdk

open System
open System.Linq
open System.Threading.Tasks
open System.Runtime.ExceptionServices


// FIXME: replace all usages of the below with native FSharp.Core's Result type (https://docs.microsoft.com/en-us/dotnet/fsharp/language-reference/results)
// when the stockmono_* lanes can use at least F# v4.5
type Either<'Val, 'Err when 'Err :> Exception> =
    | FailureResult of 'Err
    | SuccessfulValue of 'Val

module FSharpUtil =

    type internal ResultWrapper<'T>(value: 'T) =

        // hack?
        inherit Exception()

        member __.Value = value


    type IErrorMsg =
        abstract member Message: string
        abstract member ChannelBreakdown: bool

    let UnwrapOption<'T> (opt: Option<'T>) (msg: string) : 'T =
        match opt with
        | Some value -> value
        | None -> failwith <| sprintf "error unwrapping Option: %s" msg

    module AsyncExtensions =
        let private makeBoxed(job: Async<'a>) : Async<obj> =
            async {
                let! result = job
                return box result
            }

        let MixedParallel2 (a: Async<'T1>) (b: Async<'T2>) : Async<'T1 * 'T2> =
            async {
                let! results = Async.Parallel [| makeBoxed a; makeBoxed b |]
                return (unbox<'T1> results.[0]), (unbox<'T2> results.[1])
            }

        let MixedParallel3
            (a: Async<'T1>)
            (b: Async<'T2>)
            (c: Async<'T3>)
            : Async<'T1 * 'T2 * 'T3> =
            async {
                let! results =
                    Async.Parallel
                        [|
                            makeBoxed a
                            makeBoxed b
                            makeBoxed c
                        |]

                return
                    (unbox<'T1> results.[0]),
                    (unbox<'T2> results.[1]),
                    (unbox<'T3> results.[2])
            }

        let MixedParallel4
            (a: Async<'T1>)
            (b: Async<'T2>)
            (c: Async<'T3>)
            (d: Async<'T4>)
            : Async<'T1 * 'T2 * 'T3 * 'T4> =
            async {
                let! results =
                    Async.Parallel
                        [|
                            makeBoxed a
                            makeBoxed b
                            makeBoxed c
                            makeBoxed d
                        |]

                return
                    (unbox<'T1> results.[0]),
                    (unbox<'T2> results.[1]),
                    (unbox<'T3> results.[2]),
                    (unbox<'T4> results.[3])
            }

        // efficient raise
        let private RaiseResult(e: ResultWrapper<'T>) =
            Async.FromContinuations(fun (_, econt, _) -> econt e)

        /// Given sequence of computations, run them in parallel and
        /// return result of computation that finishes first.
        /// Like Async.Choice, but with no need for Option<T> types
        let WhenAny<'T>(jobs: seq<Async<'T>>) : Async<'T> =
            let wrap(job: Async<'T>) : Async<Option<'T>> =
                async {
                    let! res = job
                    return Some res
                }

            async {
                let wrappedJobs = jobs |> Seq.map wrap
                let! combinedRes = Async.Choice wrappedJobs

                match combinedRes with
                | Some x -> return x
                | None -> return failwith "unreachable"
            }

        /// Given sequence of computations, create a computation that runs them in parallel
        /// and as soon as one of sub-computations is finished, return another computation,
        /// that will wait until all sub-computations are finished, and return their results.
        let WhenAnyAndAll<'T>(jobs: seq<Async<'T>>) : Async<Async<array<'T>>> =
            let taskSource = TaskCompletionSource<unit>()

            let wrap(job: Async<'T>) =
                async {
                    let! res = job
                    taskSource.TrySetResult() |> ignore<bool>
                    return res
                }

            async {
                let allJobsInParallel =
                    jobs |> Seq.map wrap |> Async.Parallel |> Async.StartChild

                let! allJobsStarted = allJobsInParallel
                let! _ = Async.AwaitTask taskSource.Task
                return allJobsStarted
            }

    let rec private ListIntersectInternal list1 list2 offset acc currentIndex =
        match list1, list2 with
        | [], [] -> List.rev acc
        | [], _ -> List.append (List.rev acc) list2
        | _, [] -> List.append (List.rev acc) list1
        | head1 :: tail1, head2 :: tail2 ->
            if currentIndex % (int offset) = 0 then
                ListIntersectInternal
                    list1
                    tail2
                    offset
                    (head2 :: acc)
                    (currentIndex + 1)
            else
                ListIntersectInternal
                    tail1
                    list2
                    offset
                    (head1 :: acc)
                    (currentIndex + 1)

    let ListIntersect<'T>
        (list1: List<'T>)
        (list2: List<'T>)
        (offset: uint32)
        : List<'T> =
        ListIntersectInternal list1 list2 offset [] 1

    let SeqTryHeadTail<'T>(sequence: seq<'T>) : Option<'T * seq<'T>> =
        match Seq.tryHead sequence with
        | None -> None
        | Some head -> Some(head, Seq.tail sequence)

    let rec SeqAsyncTryPick<'T, 'U>
        (sequence: seq<'T>)
        (chooser: 'T -> Async<Option<'U>>)
        : Async<Option<'U>> =
        async {
            match SeqTryHeadTail sequence with
            | None -> return None
            | Some(head, tail) ->
                let! choiceOpt = chooser head

                match choiceOpt with
                | None -> return! SeqAsyncTryPick tail chooser
                | Some choice -> return Some choice
        }

    let ListAsyncTryPick<'T, 'U>
        (list: list<'T>)
        (chooser: 'T -> Async<Option<'U>>)
        : Async<Option<'U>> =
        SeqAsyncTryPick (list |> Seq.ofList) chooser

    let SleepSpan(span: TimeSpan) =
        Async.Sleep(int span.TotalMilliseconds)

    let WithTimeout (timeSpan: TimeSpan) (job: Async<'R>) : Async<Option<'R>> =
        async {
            let read =
                async {
                    let! value = job
                    return value |> SuccessfulValue |> Some
                }

            let delay =
                async {
                    let total = int timeSpan.TotalMilliseconds
                    do! Async.Sleep total
                    return FailureResult <| TimeoutException() |> Some
                }

            let! dummyOption = Async.Choice([ read; delay ])

            match dummyOption with
            | Some theResult ->
                match theResult with
                | SuccessfulValue r -> return Some r
                | FailureResult _ -> return None
            | None ->
                // none of the jobs passed to Async.Choice returns None
                return failwith "unreachable"
        }

    // FIXME: we should not need this workaround anymore when this gets addressed:
    //        https://github.com/fsharp/fslang-suggestions/issues/660
    let ReRaise(ex: Exception) : Exception =
        (ExceptionDispatchInfo.Capture ex).Throw()
        failwith "Should be unreachable"
        ex

    let rec public FindException<'T when 'T :> Exception>
        (ex: Exception)
        : Option<'T> =
        let rec findExInSeq(sq: seq<Exception>) =
            match Seq.tryHead sq with
            | Some head ->
                let found = FindException head

                match found with
                | Some ex -> Some ex
                | None -> findExInSeq <| Seq.tail sq
            | None -> None

        if null = ex then
            None
        else
            match ex with
            | :? 'T as specificEx -> Some(specificEx)
            | :? AggregateException as aggEx ->
                findExInSeq aggEx.InnerExceptions
            | _ -> FindException<'T>(ex.InnerException)

    // Searches through an exception tree and ensures that all the leaves of
    // the tree have type 'T. Returns these 'T exceptions as a sequence, or
    // otherwise re-raises the original exception if there are any non-'T-based
    // exceptions in the tree.
    let public FindSingleException<'T when 'T :> Exception>
        (ex: Exception)
        : seq<'T> =
        let rec findSingleExceptionOpt(ex: Exception) : Option<seq<'T>> =
            let rec findSingleExceptionInSeq
                (sq: seq<Exception>)
                (acc: seq<'T>)
                : Option<seq<'T>> =
                match Seq.tryHead sq with
                | Some head ->
                    match findSingleExceptionOpt head with
                    | Some exs ->
                        findSingleExceptionInSeq
                            (Seq.tail sq)
                            (Seq.concat [ acc; exs ])
                    | None -> None
                | None -> Some acc

            let findSingleInnerException(ex: Exception) : Option<seq<'T>> =
                if null = ex.InnerException then
                    None
                else
                    findSingleExceptionOpt ex.InnerException

            match ex with
            | :? 'T as specificEx -> Some <| Seq.singleton specificEx
            | :? AggregateException as aggEx ->
                findSingleExceptionInSeq aggEx.InnerExceptions Seq.empty
            | _ -> findSingleInnerException ex

        match findSingleExceptionOpt ex with
        | Some exs -> exs
        | None ->
            ReRaise ex |> ignore<Exception>
            failwith "unreachable"

    type OptionBuilder() =
        // see https://github.com/dsyme/fsharp-presentations/blob/master/design-notes/ces-compared.md#overview-of-f-computation-expressions
        member x.Bind(v, f) =
            Option.bind f v

        member x.Return v =
            Some v

        member x.ReturnFrom o =
            o

        member x.Zero() =
            None

    let option = OptionBuilder()

    let Retry<'T, 'TException when 'TException :> Exception>
        sourceFunc
        retryCount
        : Async<'T> =
        async {
            let rec retrySourceFunc currentRetryCount =
                async {
                    try
                        return! sourceFunc()
                    with
                    | ex ->
                        match FindException<'TException> ex with
                        | Some ex ->
                            if currentRetryCount = 0 then
                                return raise <| ReRaise ex

                            return! retrySourceFunc(currentRetryCount - 1)
                        | None -> return raise <| ReRaise ex
                }

            return! retrySourceFunc retryCount
        }

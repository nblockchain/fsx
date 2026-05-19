namespace Fsdk.Tests

open System
open System.Diagnostics
open System.Threading
open System.Threading.Tasks

open NUnit.Framework

open Fsdk
open Fsdk.FSharpUtil


[<TestFixture>]
type DotNetAsyncCancellation() =

    (* NOTE: these tests are not really Fsdk's tests, but tests around F#&C# async/await&cancelToken behaviours, to
             make me understand better how it works; this means that these tests will never be broken by any code that
             would be introduced in Fsdk. If they break, then Microsoft fucked up! haha *)

    [<Test>]
    member __.``assignment after Task.Delay does not await the delay obiously``
        ()
        =
        let mutable finishedDelay = false

        let SomeMethodAsync() : Task =
            let task = Task.Delay(TimeSpan.FromSeconds 1.0)
            finishedDelay <- true
            task

        let asyncJob =
            async {
                Assert.That(finishedDelay, Is.EqualTo false, "initial state")
                let task = SomeMethodAsync()
                Assert.That(finishedDelay, Is.EqualTo true, "got the task")
                do! Async.AwaitTask task

                Assert.That(
                    finishedDelay,
                    Is.EqualTo true,
                    "after awaited the task"
                )
            }

        Async.RunSynchronously asyncJob

    [<Test>]
    member __.``assignment when Task.Delay.Continue() awaits the delay``() =
        let mutable finishedDelay = false

        let SomeMethodAsync() : Task =
            Task
                .Delay(TimeSpan.FromSeconds 1.0)
                .ContinueWith(fun _ -> finishedDelay <- true)

        let asyncJob =
            async {
                Assert.That(finishedDelay, Is.EqualTo false, "initial state")
                let task = SomeMethodAsync()
                Assert.That(finishedDelay, Is.EqualTo false, "got the task")
                do! Async.AwaitTask task

                Assert.That(
                    finishedDelay,
                    Is.EqualTo true,
                    "after awaited the task"
                )
            }

        Async.RunSynchronously asyncJob

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (1)``
        ()
        =
        let mutable someCount = 1

        let SomeMethodAsync() : Task =
            Task
                .Delay(TimeSpan.FromSeconds 2.0)
                .ContinueWith(fun _ -> someCount <- someCount + 10)

        let asyncJob =
            async {
                let task = SomeMethodAsync()
                do! Async.AwaitTask task
                someCount <- someCount + 100
            }

        Async.RunSynchronously asyncJob
        Assert.That(someCount, Is.EqualTo 111, "after awaited the task")

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (2)``
        ()
        =
        // cancellation doesn't get propagated to the awaited task if it's already being awaited
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1

        let SomeMethodAsync() : Task =
            Task
                .Delay(TimeSpan.FromSeconds 3.0)
                .ContinueWith(fun _ -> newCount <- newCount + 10)

        let asyncJob =
            async {
                let task = SomeMethodAsync()
                do! Async.AwaitTask task
                newCount <- newCount + 100
            }

        let task =
            Async.StartAsTask(
                asyncJob,
                ?cancellationToken = Some cancelSource.Token
            )

        // let the task start
        Thread.Sleep(TimeSpan.FromSeconds 1.0)

        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo false)
        cancelSource.Cancel()
        Thread.Sleep(TimeSpan.FromSeconds 6.0)
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)
        Assert.That(newCount, Is.EqualTo 111, "cancellation didn't work at all")

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (3)``
        ()
        =
        // cancellation works partially because it happens before AwaitTask is called
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1

        let SomeMethodAsync() : Task =
            Task
                .Delay(TimeSpan.FromSeconds 3.0)
                .ContinueWith(fun _ -> newCount <- newCount + 10)

        let asyncJob =
            async {
                Thread.Sleep(TimeSpan.FromSeconds 2.0)
                let task = SomeMethodAsync()
                do! Async.AwaitTask task
                newCount <- newCount + 100
            }

        let task =
            Async.StartAsTask(
                asyncJob,
                ?cancellationToken = Some cancelSource.Token
            )

        // let the task start
        Thread.Sleep(TimeSpan.FromSeconds 1.0)

        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo false)
        cancelSource.Cancel()
        Thread.Sleep(TimeSpan.FromSeconds 8.0)
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

        Assert.That(newCount, Is.EqualTo 11, "cancellation worked partially")

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (4)``
        ()
        =
        // easy cancellation with an async.sleep
        let mutable newCount = 1
        use cancelSource = new CancellationTokenSource()

        let SomeMethodAsync() : Task =
            Task
                .Delay(TimeSpan.FromSeconds 2.0)
                .ContinueWith(fun _ -> newCount <- newCount + 10)

        let asyncJob =
            async {
                let task = SomeMethodAsync()
                do! Async.AwaitTask task
                do! SleepSpan <| TimeSpan.FromSeconds 2.0
                newCount <- newCount + 100
            }

        let task =
            Async.StartAsTask(
                asyncJob,
                ?cancellationToken = Some cancelSource.Token
            )

        // let the task start
        Thread.Sleep(TimeSpan.FromSeconds 1.0)

        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo false)
        cancelSource.Cancel()
        Assert.That(newCount, Is.EqualTo 1, "canceled properly, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 5.0)

        Assert.That(
            newCount,
            Is.EqualTo 11,
            "cancellation works this way partially too"
        )

        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (5)``
        ()
        =
        // immediate cancellation inside async{}
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1

        let SomeMethodAsync() : Task =
            Task
                .Delay(TimeSpan.FromSeconds 2.0)
                .ContinueWith(fun _ -> newCount <- newCount + 10)

        let asyncJob =
            async {
                let task = SomeMethodAsync()
                cancelSource.Cancel()
                do! Async.AwaitTask task
                newCount <- newCount + 100
            }

        let task =
            Async.StartAsTask(
                asyncJob,
                ?cancellationToken = Some cancelSource.Token
            )

        // let the task start
        Thread.Sleep(TimeSpan.FromSeconds 1.0)

        Assert.That(newCount, Is.EqualTo 1, "canceled properly, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 3.0)

        Assert.That(
            newCount,
            Is.EqualTo 11,
            "even if canceled early, the task is still done, after waiting"
        )

        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (6)``
        ()
        =
        let mutable newCount = 1
        use cancelSource = new CancellationTokenSource()

        let SomeMethodAsync() : Task =
            Task
                .Delay(TimeSpan.FromSeconds 1.0)
                .ContinueWith(fun _ -> newCount <- newCount + 10)

        let asyncJob =
            async {
                cancelSource.Cancel()
                let task = SomeMethodAsync()
                do! Async.AwaitTask task
                newCount <- newCount + 100
            }

        let task =
            Async.StartAsTask(
                asyncJob,
                ?cancellationToken = Some cancelSource.Token
            )

        Thread.Sleep(TimeSpan.FromSeconds 3.0)

        Assert.That(
            newCount,
            Is.EqualTo 11,
            "even if canceled before getting the task, task is done but canceled after that"
        )

        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (7)``
        ()
        =
        let mutable newCount = 1
        use cancelSource = new CancellationTokenSource()

        let asyncJob =
            async {
                cancelSource.Cancel()
                do! SleepSpan <| TimeSpan.FromSeconds 2.0
                newCount <- newCount + 100
            }

        let task =
            Async.StartAsTask(
                asyncJob,
                ?cancellationToken = Some cancelSource.Token
            )

        // let the task start
        Thread.Sleep(TimeSpan.FromSeconds 1.0)

        Assert.That(newCount, Is.EqualTo 1, "canceled properly, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 3.0)

        Assert.That(
            newCount,
            Is.EqualTo 1,
            "canceled with no awaitTask, it's properly canceled too"
        )

        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (8)``
        ()
        =
        // immediate cancellation inside task does nothing
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1

        let SomeMethodAsync() : Task =
            let task1 = Task.Delay(TimeSpan.FromSeconds 2.0)

            let task2andTask1 =
                task1.ContinueWith(fun _ ->
                    newCount <- newCount + 10
                    cancelSource.Cancel()
                )

            let task3andTask2andTask2 =
                task2andTask1.ContinueWith(fun _ ->
                    Task.Delay(TimeSpan.FromSeconds 2.0)
                )

            let allTasks =
                task3andTask2andTask2.ContinueWith(fun (_: Task) ->
                    newCount <- newCount + 100
                )

            allTasks

        let asyncJob =
            async {
                let task = SomeMethodAsync()
                do! Async.AwaitTask task
                newCount <- newCount + 1000
            }

        let task =
            Async.StartAsTask(
                asyncJob,
                ?cancellationToken = Some cancelSource.Token
            )

        // let the task start
        Thread.Sleep(TimeSpan.FromSeconds 1.0)

        Assert.That(newCount, Is.EqualTo 1, "not canceled yet, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 4.0)

        Assert.That(
            newCount,
            Is.EqualTo 1111,
            "canceled inside task doesn't really cancel!"
        )

        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it? (9)``
        ()
        =
        // immediate cancellation inside task does nothing
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1

        let SomeMethodAsync() : Task =
            let task1 = Task.Delay(TimeSpan.FromSeconds 1.0)

            let task2andTask1 =
                task1.ContinueWith(fun _ ->
                    newCount <- newCount + 10
                    cancelSource.Cancel()
                )

            let task3andTask2andTask2 =
                task2andTask1.ContinueWith(fun _ ->
                    Task.Delay(TimeSpan.FromSeconds 1.0)
                )

            let allTasks =
                task3andTask2andTask2.ContinueWith(fun (_: Task) ->
                    newCount <- newCount + 100
                )

            allTasks

        let asyncJob =
            async {
                let task = SomeMethodAsync()
                do! Async.AwaitTask task
                do! SleepSpan <| TimeSpan.FromSeconds 3.0
                newCount <- newCount + 1000
            }

        let task =
            Async.StartAsTask(
                asyncJob,
                ?cancellationToken = Some cancelSource.Token
            )

        Assert.That(newCount, Is.EqualTo 1, "not canceled yet, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 6.0)

        Assert.That(
            newCount,
            Is.EqualTo 111,
            "canceled inside task and async.sleep after awaitTask does work partially"
        )

        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it if cancelToken is passed``
        ()
        =
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1

        let SomeMethodAsync() : Task =
            let task1 = Task.Delay(TimeSpan.FromSeconds 2.0)

            let task2andTask1 =
                task1.ContinueWith(fun _ ->
                    newCount <- newCount + 10
                    cancelSource.Cancel()
                )

            let task3andTask2andTask2 =
                task2andTask1.ContinueWith(fun _ ->
                    Task.Delay(TimeSpan.FromSeconds 1.0)
                )

            let allTasks =
                task3andTask2andTask2.ContinueWith(fun (_: Task) ->
                    newCount <- newCount + 100
                )

            allTasks

        let asyncJob =
            async {
                let task = SomeMethodAsync()
                do! Async.AwaitTask task
                do! SleepSpan <| TimeSpan.FromSeconds 3.0
                newCount <- newCount + 1000
            }

        let task =
            Async.StartAsTask(
                asyncJob,
                ?cancellationToken = Some cancelSource.Token
            )

        // let the task start
        Thread.Sleep(TimeSpan.FromSeconds 1.0)

        Assert.That(newCount, Is.EqualTo 1, "not canceled yet, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 6.0)

        Assert.That(
            newCount,
            Is.EqualTo 111,
            "canceled inside task and async.sleep after awaitTask does work"
        )

        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)
        Assert.That(task.IsCanceled, Is.EqualTo true)

    [<Test>]
    member __.``cancelling async jobs cancels nested tasks awaited inside it if cancelToken is propagated``
        ()
        =
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1

        let SomeMethodAsync() : Task =
            let task1 = Task.Delay(TimeSpan.FromSeconds 2.0, cancelSource.Token)

            let task2andTask1 =
                task1.ContinueWith(
                    (fun _ ->
                        newCount <- newCount + 10
                        cancelSource.Cancel()
                    ),
                    cancelSource.Token
                )

            let task3andTask2andTask2 =
                task2andTask1.ContinueWith(
                    (fun _ ->
                        Task.Delay(TimeSpan.FromSeconds 1.0, cancelSource.Token)
                    ),
                    cancelSource.Token
                )

            let allTasks =
                task3andTask2andTask2.ContinueWith(
                    (fun (_: Task) -> newCount <- newCount + 100),
                    cancelSource.Token
                )

            allTasks

        let asyncJob =
            async {
                let task = SomeMethodAsync()
                do! Async.AwaitTask task
                do! SleepSpan <| TimeSpan.FromSeconds 3.0
                newCount <- newCount + 1000
            }

        let task =
            Async.StartAsTask(
                asyncJob,
                ?cancellationToken = Some cancelSource.Token
            )

        // let the task start
        Thread.Sleep(TimeSpan.FromSeconds 1.0)

        Assert.That(newCount, Is.EqualTo 1, "not canceled yet, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 6.0)

        Assert.That(
            newCount,
            Is.EqualTo 11,
            "canceled inside task propagating token does work"
        )

        Assert.That(task.Exception, Is.Not.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo true)
        Assert.That(task.IsCanceled, Is.EqualTo false)

    [<Test>]
    member __.``cancelling async jobs cancels nested single task awaited inside it if cancelToken is propagated!``
        ()
        =
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1

        let SomeMethodAsync() : Task<unit> =
            let job =
                async {
                    do! SleepSpan <| TimeSpan.FromSeconds 2.0
                    newCount <- newCount + 10
                    cancelSource.Cancel()
                    do! SleepSpan <| TimeSpan.FromSeconds 2.0
                    newCount <- newCount + 100
                }

            let task =
                Async.StartAsTask(
                    job,
                    ?cancellationToken = Some cancelSource.Token
                )

            task

        let asyncJob =
            async {
                let task = SomeMethodAsync()
                do! Async.AwaitTask task
                do! SleepSpan <| TimeSpan.FromSeconds 3.0
                newCount <- newCount + 1000
            }

        let task =
            Async.StartAsTask(
                asyncJob,
                ?cancellationToken = Some cancelSource.Token
            )

        // let the task start
        Thread.Sleep(TimeSpan.FromSeconds 1.0)

        Assert.That(newCount, Is.EqualTo 1, "not canceled yet, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 7.0)

        Assert.That(
            newCount,
            Is.EqualTo 11,
            "canceled inside task propagating token does work"
        )

        Assert.That(task.Exception, Is.Not.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo true)
        Assert.That(task.IsCanceled, Is.EqualTo false)

    [<Test>]
    member __.``cancelling async jobs cancels nested single task awaited inside it if cancelToken is propagated!!``
        ()
        =
        use cancelSource = new CancellationTokenSource()
        let mutable newCount = 1

        let SomeMethodAsync(cancelToken: CancellationToken) : Task<unit> =
            let job =
                async {
                    do! SleepSpan <| TimeSpan.FromSeconds 2.0
                    newCount <- newCount + 10
                    cancelSource.Cancel()
                    do! SleepSpan <| TimeSpan.FromSeconds 2.0
                    newCount <- newCount + 100
                }

            let task =
                Async.StartAsTask(job, ?cancellationToken = Some cancelToken)

            task

        let asyncJob =
            async {
                let! token = Async.CancellationToken
                let task = SomeMethodAsync token
                do! Async.AwaitTask task
                do! SleepSpan <| TimeSpan.FromSeconds 3.0
                newCount <- newCount + 1000
            }

        let task =
            Async.StartAsTask(
                asyncJob,
                ?cancellationToken = Some cancelSource.Token
            )

        // let the task start
        Thread.Sleep(TimeSpan.FromSeconds 1.0)

        Assert.That(newCount, Is.EqualTo 1, "not canceled yet, before waiting")
        Thread.Sleep(TimeSpan.FromSeconds 9.0)

        Assert.That(
            newCount,
            Is.EqualTo 11,
            "canceled inside task propagating token does work"
        )

        Assert.That(task.Exception, Is.Not.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo true)
        Assert.That(task.IsCanceled, Is.EqualTo false)

    [<Test>]
    member __.``cancel an already disposed cancellation source``() =
        let cancelSource = new CancellationTokenSource()
        cancelSource.Dispose()

        Assert.Throws<ObjectDisposedException>(fun _ -> cancelSource.Cancel())
        |> ignore<ObjectDisposedException>

    [<Test>]
    member __.``cancel token of a nested async job is the same as parent's (so F# is awesome at propagating)``
        ()
        =
        let someRandomNumber = 333

        let nestedAsyncJob(parentCancelToken: CancellationToken) =
            async {
                let! currentCancelToken = Async.CancellationToken

                Assert.That(
                    currentCancelToken,
                    Is.Not.EqualTo CancellationToken.None,
                    "!=None1"
                )

                Assert.That(
                    currentCancelToken.GetHashCode(),
                    Is.EqualTo(parentCancelToken.GetHashCode()),
                    "hashcode1"
                )
                //Assert.That(Object.ReferenceEquals(currentCancelToken, parentCancelToken), Is.EqualTo true, "obj.ref=1")
                Assert.That(
                    currentCancelToken,
                    Is.EqualTo parentCancelToken,
                    "equality1"
                )

                return someRandomNumber
            }

        let rootAsyncJob =
            async {
                let! currentRootCancelToken = Async.CancellationToken
                let! resultFromNestedJob = nestedAsyncJob currentRootCancelToken
                Assert.That(resultFromNestedJob, Is.EqualTo someRandomNumber)
            }

        Async.RunSynchronously rootAsyncJob

        let nestedAsyncJobWithSomeSynchronousExecution
            (parentCancelToken: CancellationToken)
            =
            Console.WriteLine "foobarbaz"

            async {
                let! currentCancelToken = Async.CancellationToken

                Assert.That(
                    currentCancelToken,
                    Is.Not.EqualTo CancellationToken.None,
                    "!=None2"
                )

                Assert.That(
                    currentCancelToken.GetHashCode(),
                    Is.EqualTo(parentCancelToken.GetHashCode()),
                    "hashcode2"
                )
                //Assert.That(Object.ReferenceEquals(currentCancelToken, parentCancelToken), Is.EqualTo true, "obj.ref=2")
                Assert.That(
                    currentCancelToken,
                    Is.EqualTo parentCancelToken,
                    "equality2"
                )

                return someRandomNumber
            }

        let rootAsyncJob2 =
            async {
                let! currentRootCancelToken = Async.CancellationToken

                let! resultFromNestedJob =
                    nestedAsyncJobWithSomeSynchronousExecution
                        currentRootCancelToken

                Assert.That(resultFromNestedJob, Is.EqualTo someRandomNumber)
            }

        Async.RunSynchronously rootAsyncJob2

        use cancelSource = new CancellationTokenSource()

        let rootAsyncJob3 =
            async {
                let! currentRootCancelToken = Async.CancellationToken

                Assert.That(
                    currentRootCancelToken,
                    Is.Not.EqualTo CancellationToken.None,
                    "!=None5"
                )

                Assert.That(
                    currentRootCancelToken.GetHashCode(),
                    Is.EqualTo(cancelSource.Token.GetHashCode()),
                    "hashcode5"
                )
                //Assert.That(Object.ReferenceEquals(currentCancelToken, parentCancelToken), Is.EqualTo true, "obj.ref=5")
                Assert.That(
                    currentRootCancelToken,
                    Is.EqualTo cancelSource.Token,
                    "equality5"
                )

                let! resultFromNestedJob =
                    nestedAsyncJobWithSomeSynchronousExecution
                        currentRootCancelToken

                Assert.That(resultFromNestedJob, Is.EqualTo someRandomNumber)
            }

        let task =
            Async.StartAsTask(
                rootAsyncJob3,
                ?cancellationToken = Some cancelSource.Token
            )

        task.Wait()
        Assert.That(task.Exception, Is.EqualTo null)
        Assert.That(task.IsFaulted, Is.EqualTo false)

    [<Test>]
    member __.``cancel task after success doesn't affect it, result can still be retreived'``
        ()
        =
        use cancellationSource = new CancellationTokenSource()
        let token = cancellationSource.Token

        let SomeMethodAsync1() : Async<int> =
            async {
                do! SleepSpan <| TimeSpan.FromSeconds 1.0
                return 1
            }

        let task1 =
            Async.StartAsTask(
                SomeMethodAsync1(),
                ?cancellationToken = Some token
            )

        let SomeMethodAsync2() : Async<int> =
            async {
                do! SleepSpan <| TimeSpan.FromSeconds 2.0
                return 2
            }

        let task2 = Async.StartAsTask(SomeMethodAsync2())
        let taskToGetTheFastestTask = Task.WhenAny([ task1; task2 ])
        let fastestTask = taskToGetTheFastestTask.Result
        Assert.That(task1, Is.EqualTo fastestTask)

        cancellationSource.Cancel()
        Assert.That(fastestTask.Result, Is.EqualTo 1)

    [<Test>]
    member __.``cancel fastest task still makes Task.WhenAny choose the fastest even if it was cancelled``
        ()
        =
        use cancellationSource = new CancellationTokenSource()
        let token = cancellationSource.Token

        let SomeMethodAsync1() : Async<int> =
            async {
                do! SleepSpan <| TimeSpan.FromSeconds 1.0
                return 1
            }

        let task1 =
            Async.StartAsTask(
                SomeMethodAsync1(),
                ?cancellationToken = Some token
            )

        let SomeMethodAsync2() : Async<int> =
            async {
                do! SleepSpan <| TimeSpan.FromSeconds 2.0
                return 2
            }

        let task2 = Async.StartAsTask(SomeMethodAsync2())
        cancellationSource.Cancel()

        let taskToGetTheFastestTask = Task.WhenAny([ task1; task2 ])
        let fastestTask = taskToGetTheFastestTask.Result
        Assert.That(task1, Is.EqualTo fastestTask)

        let ex =
            Assert.Throws<AggregateException>(fun _ ->
                Console.WriteLine fastestTask.Result
            )

        Assert.That(
            (FSharpUtil.FindException<TaskCanceledException> ex)
                .IsSome,
            Is.EqualTo true
        )

    [<Test>]
    member __.``check if we can query .IsCancellationRequested after cancelling and disposing``
        ()
        =
        let cancellationSource = new CancellationTokenSource()
        let token = cancellationSource.Token

        let SomeMethodAsync1() : Async<int> =
            async {
                do! SleepSpan <| TimeSpan.FromSeconds 1.0
                return 1
            }

        let task =
            Async.StartAsTask(
                SomeMethodAsync1(),
                ?cancellationToken = Some token
            )

        cancellationSource.Cancel()

        let ex =
            Assert.Throws<AggregateException>(fun _ ->
                Console.WriteLine task.Result
            )

        Assert.That(
            (FSharpUtil.FindException<TaskCanceledException> ex)
                .IsSome,
            Is.EqualTo true
        )

        Assert.That(cancellationSource.IsCancellationRequested, Is.EqualTo true)
        cancellationSource.Dispose()
        Assert.That(cancellationSource.IsCancellationRequested, Is.EqualTo true)

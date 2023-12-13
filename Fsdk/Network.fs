namespace Fsdk

open System
open System.IO
open System.Net
open System.Net.NetworkInformation
open System.Net.Sockets
open System.Text
open System.Linq

open Process

module Network =

    let DownloadString(uri: Uri) =
#if !LEGACY_FRAMEWORK
        use httpClient = new System.Net.Http.HttpClient()
        httpClient.GetAsync uri |> Async.AwaitTask
#else
        use webClient = new WebClient()
        webClient.DownloadStringTaskAsync uri |> Async.AwaitTask
#endif

    let DownloadFileTo (uri: Uri) (resultFile: FileInfo) : Async<FileInfo> =
        async {
            if resultFile.Exists then
                Console.WriteLine(
                    "File '{0}' already downloaded",
                    resultFile.Name
                )

                return resultFile
            else
                Console.WriteLine(
                    "File '{0}' not found, going to start download...",
                    resultFile.Name
                )
#if !LEGACY_FRAMEWORK
                // adapted code from https://stackoverflow.com/questions/20661652/progress-bar-with-httpclient/46497896#46497896
                use client = new System.Net.Http.HttpClient()
                // Get the http headers first to examine the content length
                use! response =
                    client.GetAsync(
                        uri,
                        System.Net.Http.HttpCompletionOption.ResponseHeadersRead
                    )
                    |> Async.AwaitTask

                let contentLength = response.Content.Headers.ContentLength

                if contentLength.HasValue then
                    printfn
                        "Starting download of %dMB..."
                        (contentLength.Value / 1000000L)

                use outputStream = resultFile.OpenWrite()

                use! download =
                    response.Content.ReadAsStreamAsync() |> Async.AwaitTask

                let buffer = Array.zeroCreate<byte> 81920

                let rec downloadNextPart totalBytesRead =
                    async {
                        let! bytesRead =
                            download.AsyncRead(buffer, 0, buffer.Length)

                        if bytesRead <> 0 then
                            do! outputStream.AsyncWrite(buffer, 0, bytesRead)
                            let newTotalBytesRead = totalBytesRead + bytesRead
                            // report progress
                            if contentLength.HasValue then
                                printfn
                                    "Downloaded %.3f of %.3f MB"
                                    (float(newTotalBytesRead) / 1000000.0)
                                    (float(contentLength.Value) / 1000000.0)

                            return! downloadNextPart newTotalBytesRead
                        else
                            return ()
                    }

                do! downloadNextPart 0

                return resultFile
#else
                use webClient = new WebClient()

                let lockObj = new Object()
                let mutable firstProgressEvent = true

                let onProgress
                    (progressEventArgs: DownloadProgressChangedEventArgs)
                    =
                    lock
                        lockObj
                        (fun _ ->
                            if firstProgressEvent then
                                Console.WriteLine(
                                    "Starting download of {0}MB...",
                                    (progressEventArgs.TotalBytesToReceive
                                     / 1000000L)
                                )

                            firstProgressEvent <- false
                        )

                webClient.DownloadProgressChanged.Subscribe onProgress |> ignore

                let task =
                    webClient.DownloadFileTaskAsync(uri, resultFile.FullName)

                do! Async.AwaitTask task
                return resultFile
#endif
        }

    let DownloadFile(uri: Uri) =
        DownloadFileTo
            uri
            (FileInfo(
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    Path.GetFileName uri.LocalPath
                )
            ))

    let DownloadFileWithWGet(uri: Uri) : FileInfo =
        let resultFile =
            new FileInfo(
                Path.Combine(
                    Directory.GetCurrentDirectory(),
                    Path.GetFileName(uri.LocalPath)
                )
            )

        if resultFile.Exists then
            Console.WriteLine("File '{0}' already downloaded", resultFile.Name)
        else
            Console.WriteLine(
                "File '{0}' not found, going to start download...",
                resultFile.Name
            )

            let wgetArgs =
                sprintf
                    "--output-document=%s %s"
                    (Path.GetFileName(uri.LocalPath))
                    (uri.ToString())

            let wgetProc =
                Process.Execute(
                    {
                        Command = "wget"
                        Arguments = wgetArgs
                    },
                    Echo.All
                )

            wgetProc.UnwrapDefault() |> ignore<string>

        resultFile

    (** TODO: add tests and simplify the below functions related to IsMonoTlsProblem **)
    let private RemoveFirst(aseq: seq<Type>) : seq<Type> =
        Seq.skip (1) aseq

    let private RemoveLast(aseq: seq<Type>) : seq<Type> =
        let count = aseq.Count()
        Seq.take (count - 1) aseq

    let rec private SomeAreWebException(aseq: seq<Type>) : bool =
        Seq.exists (fun exType -> (exType = typedefof<WebException>)) aseq

    let private IsMonoTlsProblemException(exceptionType: Type) : bool =
        exceptionType.FullName = "Mono.Security.Protocol.Tls.TlsException"

    let rec private ToChain(ex: Exception) : seq<Type> =
        if (ex = null) then
            Seq.empty<Type>
        else
            seq {
                yield ex.GetType()
                yield! ToChain(ex.InnerException)
            }


    (* THE BEAST THAT WE'RE TRYING TO RECOGNIZE BELOW:

 System.AggregateException: One or more errors occurred. ---> System.Net.WebException: Error: SendFailure (Error writing headers) ---> System.Net.WebException: Error writing headers ---> System.IO.IOException: The authentication or decryption has failed. ---> Mono.Security.Protocol.Tls.TlsException: The authentication or decryption has failed.
  at Mono.Security.Protocol.Tls.RecordProtocol.EndReceiveRecord (IAsyncResult asyncResult) <0x41179bd0 + 0x0010b> in <filename unknown>:0
  at Mono.Security.Protocol.Tls.SslClientStream.SafeEndReceiveRecord (IAsyncResult ar, Boolean ignoreEmpty) <0x41179b10 + 0x0002b> in <filename unknown>:0
  at Mono.Security.Protocol.Tls.SslClientStream.NegotiateAsyncWorker (IAsyncResult result) <0x41176970 + 0x00227> in <filename unknown>:0
  --- End of inner exception stack trace ---
  at System.Net.WebConnection.EndWrite (System.Net.HttpWebRequest request, Boolean throwOnError, IAsyncResult result) <0x4117b620 + 0x00207> in <filename unknown>:0
  at System.Net.WebConnectionStream+<SetHeadersAsync>c__AnonStorey1.<>m__0 (IAsyncResult r) <0x4117af20 + 0x0013b> in <filename unknown>:0
  --- End of inner exception stack trace ---
  --- End of inner exception stack trace ---
  at System.Net.HttpWebRequest.EndGetResponse (IAsyncResult asyncResult) <0x4117c6b0 + 0x0019f> in <filename unknown>:0
  at System.Net.WebClient.GetWebResponse (System.Net.WebRequest request, IAsyncResult result) <0x4117c630 + 0x00028> in <filename unknown>:0
  at System.Net.WebClient.DownloadBitsResponseCallback (IAsyncResult result) <0x4117c190 + 0x000cb> in <filename unknown>:0
  --- End of inner exception stack trace ---
  at System.Threading.Tasks.Task.ThrowIfExceptional (Boolean includeTaskCanceledExceptions) <0x7fdc4949d920 + 0x00037> in <filename unknown>:0
  at System.Threading.Tasks.Task.Wait (Int32 millisecondsTimeout, CancellationToken cancellationToken) <0x7fdc4949ed90 + 0x000c7> in <filename unknown>:0
  at System.Threading.Tasks.Task.Wait () <0x7fdc4949ec80 + 0x00028> in <filename unknown>:0
  at FSI_0005.Gatecoin.Infrastructure.Network.DownloadFile (System.Uri uri) <0x4112f840 + 0x00166> in <filename unknown>:0
  at <StartupCode$FSI_0006>.$FSI_0006.main@ () <0x41122e40 + 0x00327> in <filename unknown>:0
  at (wrapper managed-to-native) System.Reflection.MonoMethod:InternalInvoke (System.Reflection.MonoMethod,object,object[],System.Exception&)
  at System.Reflection.MonoMethod.Invoke (System.Object obj, BindingFlags invokeAttr, System.Reflection.Binder binder, System.Object[] parameters, System.Globalization.CultureInfo culture) <0x7fdc495ab9e0 + 0x000a1> in <filename unknown>:0
---> (Inner Exception #0) System.Net.WebException: Error: SendFailure (Error writing headers) ---> System.Net.WebException: Error writing headers ---> System.IO.IOException: The authentication or decryption has failed. ---> Mono.Security.Protocol.Tls.TlsException: The authentication or decryption has failed.
  at Mono.Security.Protocol.Tls.RecordProtocol.EndReceiveRecord (IAsyncResult asyncResult) <0x41179bd0 + 0x0010b> in <filename unknown>:0
  at Mono.Security.Protocol.Tls.SslClientStream.SafeEndReceiveRecord (IAsyncResult ar, Boolean ignoreEmpty) <0x41179b10 + 0x0002b> in <filename unknown>:0
  at Mono.Security.Protocol.Tls.SslClientStream.NegotiateAsyncWorker (IAsyncResult result) <0x41176970 + 0x00227> in <filename unknown>:0
  --- End of inner exception stack trace ---
  at System.Net.WebConnection.EndWrite (System.Net.HttpWebRequest request, Boolean throwOnError, IAsyncResult result) <0x4117b620 + 0x00207> in <filename unknown>:0
  at System.Net.WebConnectionStream+<SetHeadersAsync>c__AnonStorey1.<>m__0 (IAsyncResult r) <0x4117af20 + 0x0013b> in <filename unknown>:0
  --- End of inner exception stack trace ---
  --- End of inner exception stack trace ---
  at System.Net.HttpWebRequest.EndGetResponse (IAsyncResult asyncResult) <0x4117c6b0 + 0x0019f> in <filename unknown>:0
  at System.Net.WebClient.GetWebResponse (System.Net.WebRequest request, IAsyncResult result) <0x4117c630 + 0x00028> in <filename unknown>:0
  at System.Net.WebClient.DownloadBitsResponseCallback (IAsyncResult result) <0x4117c190 + 0x000cb> in <filename unknown>:0 <---

*)
    let private IsMonoTlsProblem(ex: Exception) : bool =
        let chain = ToChain(ex)

        let isFirstExceptionAnAggregate =
            (chain.First() = typedefof<AggregateException>)

        let chainInBetween =
            if isFirstExceptionAnAggregate then
                RemoveLast(RemoveFirst(chain))
            else
                RemoveLast(chain)

        let someExceptionsInBetweenAreWebExceptions =
            SomeAreWebException(chainInBetween)

        isFirstExceptionAnAggregate
        && someExceptionsInBetweenAreWebExceptions
        && IsMonoTlsProblemException(chain.Last())

    let private DownloadFileIgnoringSslCertificates
        (uri: Uri)
        : Async<FileInfo> =
        ServicePointManager.ServerCertificateValidationCallback <-
            System.Net.Security.RemoteCertificateValidationCallback(fun _ _ _ _ ->
                true
            )

        async {
            try
                return! DownloadFile uri
            with
            | ex when IsMonoTlsProblem(ex) ->
                Console.Error.WriteLine("Falling back to WGET download")
                return DownloadFileWithWGet uri
        }

#if LEGACY_FRAMEWORK
    let SafeDownloadFile(uri: Uri, sha256sum: string) : Async<FileInfo> =
        async {
            let! resultFile =
                try
                    let result = DownloadFile uri
                    Console.WriteLine "Download finished"
                    result
                with
                | ex when IsMonoTlsProblem ex ->
                    Console.Error.WriteLine
                        "Falling back to certificate-less safe download"

                    DownloadFileIgnoringSslCertificates uri

            if not(sha256sum = Misc.CalculateSHA256 resultFile) then
                failwith(
                    sprintf
                        "%s: SHA256 hash doesn't match, beware possible previous unfinished download, or M.I.T.M.A.: Man In The Middle Attack"
                        resultFile.FullName
                )

            return resultFile
        }

    [<Obsolete("Rather use safer SafeDownloadFile() which receives SHA256SUM instead of MD5")>]
    let SafeDownloadFileMD5(uri: Uri, md5sum: string) : Async<FileInfo> =
        async {
            let! resultFile =
                try
                    let result = DownloadFile uri
                    Console.WriteLine "Download finished"
                    result
                with
                | ex when IsMonoTlsProblem ex ->
                    Console.Error.WriteLine
                        "Falling back to certificate-less safe download"

                    DownloadFileIgnoringSslCertificates uri

            if not(md5sum = Misc.CalculateMD5 resultFile) then
                failwith(
                    sprintf
                        "%s: MD5 hash doesn't match, beware possible previous unfinished download, or M.I.T.M.A.: Man In The Middle Attack"
                        resultFile.FullName
                )

            return resultFile
        }
#endif

    let IsPortOpen(host: string, port: int) : bool =
        let canConnect =
            try
                use client = new TcpClient()
                let result = client.BeginConnect(host, port, null, null)

                let success =
                    result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5.0))

                match (success && client.Connected) with
                | false -> false
                | true ->
                    client.EndConnect(result)
                    true
            with
            | _ ->
                ()
                false

        canConnect

    // http://stackoverflow.com/a/40544158/544947
    let private IsIpv6AddressPrivate(address: IPAddress) =
        if (address.AddressFamily = AddressFamily.InterNetwork) then
            invalidArg "address" "address must be IPv6"

        // The original IPv6 Site Local addresses (fec0::/10) are deprecated. Unfortunately IsIPv6SiteLocal only checks for the original deprecated version:
        else if address.IsIPv6SiteLocal then
            true
        else
            let addressAsString = address.ToString()

            // equivalent of 127.0.0.1 in IPv6
            if (addressAsString = "::1") then
                true
            else
                let firstWord =
                    addressAsString.Split(
                        [| ':' |],
                        StringSplitOptions.RemoveEmptyEntries
                    ).[0]
                // These days Unique Local Addresses (ULA) are used in place of Site Local.
                // ULA has two variants:
                //      fc00::/8 is not defined yet, but might be used in the future for internal-use addresses that are registered in a central place (ULA Central).
                //      fd00::/8 is in use and does not have to registered anywhere.
                if (firstWord.Length >= 4 && firstWord.Substring(0, 2) = "fc")
                   || (firstWord.Length >= 4 && firstWord.Substring(0, 2) = "fd")
                   ||
                   // Link local addresses (prefixed with fe80) are not routable
                   (firstWord = "fe80")
                   ||
                   // Discard Prefix
                   (firstWord = "100") then
                    true
                else
                    false

    let private IsIpv4AddressPrivate(address: IPAddress) =
        let ipParts = Misc.SimpleStringSplit(address.ToString(), ".")

        let secondPart = Int32.Parse(ipParts.[1])

        if (ipParts.[0] = "10") then
            true
        else if (ipParts.[0] = "192" && secondPart = 168) then
            true
        else if (ipParts.[0] = "172" && secondPart >= 16 && secondPart <= 31) then
            true
        else if (address.ToString() = "127.0.0.1") then
            true
        else
            false

    // http://stackoverflow.com/a/799069/544947
    let IsIpAddressPrivate(address: IPAddress) =
        match address.AddressFamily with
        | AddressFamily.InterNetwork -> IsIpv4AddressPrivate(address)
        | AddressFamily.InterNetworkV6 -> IsIpv6AddressPrivate(address)
        | _ -> failwith("Unknown address family")

    let DoesHostHavePrivateIp(host: string) =
        if (host = "localhost") then
            true
        else
            let resolvedIpAddresses = Dns.GetHostAddresses(host)

            if (resolvedIpAddresses.Length = 0) then
                failwith(String.Format("Could not resolve {0}", host))

            let firstIpAddress = resolvedIpAddresses.[0]
            IsIpAddressPrivate(firstIpAddress)

    let GetHostnameOfThisServer() =
        Dns.GetHostName()

    let GetPrivateIpOfThisServer() =
        if not(NetworkInterface.GetIsNetworkAvailable()) then
            failwith "No network available at this moment"

        Dns
            .GetHostEntry(Dns.GetHostName())
            .AddressList
            .FirstOrDefault(fun ip ->
                ip.AddressFamily = AddressFamily.InterNetwork
            )

    let private ServerNameAndAddress() =
        String.Format(
            "{0}({1})",
            GetHostnameOfThisServer(),
            GetPrivateIpOfThisServer()
        )

    // this is a translation of doing this in unix (assuming initialVersion="0.1.0"):
    // 0.1.0--date`date +%Y%m%d-%H%M`.git-`git rev-parse --short=7 HEAD`
    let GetNugetPrereleaseVersionFromBaseVersion(baseVersion: string) =
        let initialVersion =
            let versionSplit = baseVersion.Split '.'

            if versionSplit.Length = 4 && versionSplit.[3] = "0" then
                String.Join(".", versionSplit.Take 3)
            else
                baseVersion

        let dateSegment =
            sprintf "date%s" (DateTime.UtcNow.ToString "yyyyMMdd-hhmm")

        let gitHash = Git.GetLastCommit()

        if isNull gitHash then
            Console.Error.WriteLine "Not in a git repository?"
            Environment.Exit 2

        let gitHashDefaultShortLength = 7
        let gitShortHash = gitHash.Substring(0, gitHashDefaultShortLength)
        let gitSegment = sprintf "git-%s" gitShortHash

        let finalVersion =
            sprintf "%s--%s.%s" initialVersion dateSegment gitSegment

        finalVersion

    let NugetDownloadUrl =
        "https://dist.nuget.org/win-x86-commandline/v5.4.0/nuget.exe"

    let internal DownloadNugetExeInternal(targetFile: FileInfo) =
        if not targetFile.Directory.Exists then
            targetFile.Directory.Create()

        DownloadFileTo (Uri NugetDownloadUrl) targetFile
        |> Async.RunSynchronously
        |> ignore<FileInfo>

#if !LEGACY_FRAMEWORK
    [<Obsolete "Rather call 'dotnet restore' or 'dotnet nuget'">]
#endif
    let DownloadNugetExe(targetFile: FileInfo) =
        DownloadNugetExeInternal targetFile

    let internal CreateNugetCommand (nugetExe: FileInfo) (args: string) =
        let platform = Misc.GuessPlatform()

        if platform = Misc.Platform.Windows then
            {
                Command = nugetExe.FullName
                Arguments = args
            }
        else
            {
                Command = "mono"
                Arguments = sprintf "%s %s" nugetExe.FullName args
            }

    let internal RunNugetCommandInternal
        (nugetExe: FileInfo)
        (command: string)
        (echo: Echo)
        (safe: bool)
        : ProcessResult =
        DownloadNugetExeInternal nugetExe

#if !LEGACY_FRAMEWORK
        Console.Error.WriteLine
            "WARNING: using nuget.exe is deprecated when using .NET6 or newer, consider using dotnet restore instead"
#endif

        let cmd = CreateNugetCommand nugetExe command
        let proc = Process.Execute(cmd, echo)

        if safe then
            proc.UnwrapDefault() |> ignore<string>

        proc

#if !LEGACY_FRAMEWORK
    [<Obsolete "Rather call 'dotnet restore' or 'dotnet nuget'">]
#endif
    let RunNugetCommand
        (nugetExe: FileInfo)
        (command: string)
        (echo: Echo)
        (safe: bool)
        =
        RunNugetCommandInternal nugetExe command echo safe

#if !LEGACY_FRAMEWORK
    [<Obsolete "Rather call 'dotnet restore' or 'dotnet nuget'">]
#endif
    let InstallNugetPackage
        (nugetExe: FileInfo)
        (outputDirectory: DirectoryInfo)
        (pkgName: string)
        (maybeVersion: Option<string>)
        (echo: Echo)
        : ProcessResult =

        if not outputDirectory.Exists then
            outputDirectory.Create()

        let maybeVersionFlag =
            match maybeVersion with
            | None -> String.Empty
            | Some version -> sprintf "-Version %s" version

        RunNugetCommandInternal
            nugetExe
            (sprintf
                "install %s %s -OutputDirectory %s"
                pkgName
                maybeVersionFlag
                outputDirectory.FullName)
            echo
            true

    let private SLACK_WEBHOOK_URI =
        "https://hooks.slack.com/services/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"

#if LEGACY_FRAMEWORK
    let SlackNotify(message: string) =
        let textToSend =
            if not(message.Contains(Environment.NewLine)) then
                sprintf
                    "*[%s] %s: %s *"
                    (DateTime.Now.ToString())
                    (ServerNameAndAddress())
                    message
            else
                let lines =
                    message.Split(
                        [| Environment.NewLine |],
                        StringSplitOptions.None
                    )

                let firstLine = lines.First()
                let rest = lines.Skip(1)

                sprintf
                    "*[%s] %s: %s *%s%s"
                    (DateTime.Now.ToString())
                    (ServerNameAndAddress())
                    firstLine
                    Environment.NewLine
                    (String.Join(Environment.NewLine, rest))

        let webClient = new WebClient()

        webClient.Headers.Add(
            "Content-Type",
            "application/x-www-form-urlencoded"
        )

        let escapedText =
            textToSend
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")

        let json =
            sprintf
                "{ \"channel\": \"#it_team\", \"text\": \"%s\" }"
                escapedText

        let request = Encoding.UTF8.GetBytes("payload=" + json)

        try
            webClient.UploadData(SLACK_WEBHOOK_URI, "POST", request) |> ignore
        with
        | ex ->
            raise
            <| Exception(
                sprintf "Problem when trying to upload '%s' to Slack" json,
                ex
            )
#endif

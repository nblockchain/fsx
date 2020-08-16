namespace GWallet.Backend

open System
open System.Net

open FSharp.Data

type Currency =
    | BTC
    | LTC
    | ETH
    | ETC
    | DAI
    | SAI

module FiatValueEstimation =
    let private PERIOD_TO_CONSIDER_PRICE_STILL_FRESH = TimeSpan.FromMinutes 2.0

    type CoinCapProvider = JsonProvider<"""
    {
      "data": {
        "id": "bitcoin",
        "symbol": "BTC",
        "currencySymbol": "x",
        "type": "crypto",
        "rateUsd": "6444.3132749056076909"
      },
      "timestamp": 1536347871542
    }
    """>

    type PriceProvider =
        | CoinCap
        | CoinGecko

    let private QueryOnlineInternal currency (provider: PriceProvider): Async<Option<string*string>> = async {
        use webClient = new WebClient()
        let tickerName =
            match currency,provider with
            | Currency.BTC,_ -> "bitcoin"
            | Currency.LTC,_ -> "litecoin"
            | Currency.ETH,_ | Currency.SAI,_ -> "ethereum"
            | Currency.ETC,_ -> "ethereum-classic"
            | Currency.DAI,PriceProvider.CoinCap -> "multi-collateral-dai"
            | Currency.DAI,_ -> "dai"
        try
            let baseUrl =
                match provider with
                | PriceProvider.CoinCap ->
                    sprintf "https://api.coincap.io/v2/rates/%s" tickerName
                | PriceProvider.CoinGecko ->
                    sprintf "https://api.coingecko.com/api/v3/simple/price?ids=%s&vs_currencies=usd" tickerName
            let uri = Uri baseUrl
            let task = webClient.DownloadStringTaskAsync uri
            let! res = Async.AwaitTask task
            return Some (tickerName,res)
        with
        | ex ->
            if ex.GetType() = typeof<WebException> then
                return None
            else
                return raise ex
    }

    let private QueryCoinCap currency = async {
        let! maybeJson = QueryOnlineInternal currency PriceProvider.CoinCap
        match maybeJson with
        | None -> return None
        | Some (_, json) ->
            try
                let tickerObj = CoinCapProvider.Parse json
                return Some tickerObj.Data.RateUsd
            with
            | ex ->
                if currency = ETC then
                    // interestingly this can throw in CoinCap because retreiving ethereum-classic doesn't work...
                    return None
                else
                    return raise ex
    }

    let private QueryCoinGecko currency = async {
        let! maybeJson = QueryOnlineInternal currency PriceProvider.CoinGecko
        match maybeJson with
        | None -> return None
        | Some (ticker, json) ->
            // try to parse this as an example: {"bitcoin":{"usd":7952.29}}
            let parsedJsonObj = FSharp.Data.JsonValue.Parse json
            let usdPrice =
                match parsedJsonObj.TryGetProperty ticker with
                | None -> failwith <| sprintf "Could not pre-parse %s" json
                | Some innerObj ->
                    match innerObj.TryGetProperty "usd" with
                    | None -> failwith <| sprintf "Could not parse %s" json
                    | Some value -> value.AsDecimal()
            return Some usdPrice
    }

    let private RetrieveOnline currency = async {
        let coinGeckoJob = QueryCoinGecko currency
        let coinCapJob = QueryCoinCap currency
        let! bothJobs = Async.Parallel [ coinGeckoJob ; coinCapJob ]
        let maybeUsdPriceFromCoinGecko, maybeUsdPriceFromCoinCap = bothJobs.[0], bothJobs.[1]
        let result =
            match maybeUsdPriceFromCoinGecko, maybeUsdPriceFromCoinCap with
            | None, None -> None
            | Some usdPriceFromCoinGecko, None ->
                Some usdPriceFromCoinGecko
            | None, Some usdPriceFromCoinCap ->
                Some usdPriceFromCoinCap
            | Some usdPriceFromCoinGecko, Some usdPriceFromCoinCap ->
                let average = (usdPriceFromCoinGecko + usdPriceFromCoinCap) / 2m
                Some average

        let realResult =
            match result with
            | Some price ->
                let realPrice =
                    if currency = Currency.SAI then
                        let ethMultiplied = price * 0.0053m
                        ethMultiplied
                    else
                        price
                realPrice |> Some
            | None -> None
        return realResult
    }


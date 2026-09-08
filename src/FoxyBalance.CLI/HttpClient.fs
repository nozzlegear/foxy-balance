namespace FoxyBalance.CLI

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Threading.Tasks
open FoxyBalance.CLI.Domain
open FoxyBalance.CLI.TokenStore
open Thoth.Json.Core



/// HTTP client for the FoxyBalance API with automatic token refresh on 401.
type FoxyBalanceClient(baseUrl: string) =

    let createClient () =
        let client = new HttpClient(HttpHandler.create ())
        client.BaseAddress <- Uri(baseUrl.TrimEnd('/'))
        client.Timeout <- TimeSpan.FromSeconds(30.0)
        client.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue("application/json"))
        client

    /// Extract the `data` field from a HAL response using a Thoth decoder.
    static member private extractData (decoder: Decoder<'T>) (body: string) : Result<'T, string> =
        Codecs.extractData decoder body

    /// Make an authenticated request. If `forceRefresh` is true, refresh the token before making the request.
    /// On 401, refresh once and retry.
    member private self.requestAsync
        (method: HttpMethod)
        (path: string)
        (body: string option)
        (forceRefresh: bool)
        : Async<Result<string, string>> =
        async {
            let! tokenResult = TokenRefresh.getValidAccessToken forceRefresh

            match tokenResult with
            | Error e -> return Error e
            | Ok config ->
                use client = createClient ()
                client.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", config.AccessToken)

                use request = new HttpRequestMessage(method, path)

                match body with
                | Some json ->
                    request.Content <- new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                | None -> ()

                try
                    let! response = client.SendAsync(request) |> Async.AwaitTask
                    let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                    if response.IsSuccessStatusCode then
                        return Ok responseBody
                    elif response.StatusCode = System.Net.HttpStatusCode.Unauthorized && not forceRefresh then
                        let! retryResult = self.requestAsync method path body true
                        return retryResult
                    else
                        let errorMsg =
                            match Codecs.deserialize Codecs.apiErrorDecoder responseBody with
                            | Ok apiError -> apiError.Error
                            | Error _ -> $"HTTP {int response.StatusCode}: {responseBody}"
                        return Error errorMsg
                with ex ->
                    let rec innerMsg (e: exn) =
                        match e with
                        | :? AggregateException as agg when agg.InnerException <> null -> innerMsg agg.InnerException
                        | :? System.Net.Http.HttpRequestException as hre when hre.InnerException <> null -> innerMsg hre.InnerException
                        | _ -> e.Message
                    return Error $"Network error: {innerMsg ex}"
        }

    // ---- Public API methods (data only, no links) ----

    member self.GetAsync(decoder: Decoder<'T>, path: string) : Async<Result<'T, string>> =
        async {
            let! result = self.requestAsync HttpMethod.Get path None false
            return result |> Result.bind (FoxyBalanceClient.extractData decoder)
        }

    member self.GetCollectionAsync(decoder: Decoder<'T>, path: string) : Async<Result<'T list, string>> =
        async {
            let! result = self.requestAsync HttpMethod.Get path None false
            return
                result
                |> Result.bind (fun body ->
                    Codecs.deserializeHalCollection decoder body
                    |> Result.map (fun hal -> hal.Items |> List.map (fun r -> r.Data)))
        }

    member self.PostAsync(encoder: Encoder<'Req>, decoder: Decoder<'T>, path: string, body: 'Req) : Async<Result<'T, string>> =
        async {
            let json = Codecs.serialize encoder body
            let! result = self.requestAsync HttpMethod.Post path (Some json) false
            return result |> Result.bind (FoxyBalanceClient.extractData decoder)
        }

    member self.PostWithoutBodyAsync(decoder: Decoder<'T>, path: string) : Async<Result<'T, string>> =
        async {
            let! result = self.requestAsync HttpMethod.Post path None false
            return result |> Result.bind (FoxyBalanceClient.extractData decoder)
        }

    member self.PutAsync(encoder: Encoder<'Req>, decoder: Decoder<'T>, path: string, body: 'Req) : Async<Result<'T, string>> =
        async {
            let json = Codecs.serialize encoder body
            let! result = self.requestAsync HttpMethod.Put path (Some json) false
            return result |> Result.bind (FoxyBalanceClient.extractData decoder)
        }

    member self.DeleteAsync(path: string) : Async<Result<unit, string>> =
        async {
            let! result = self.requestAsync HttpMethod.Delete path None false
            return result |> Result.map (fun _ -> ())
        }

    // ---- Resource-with-links methods (return full HAL resources including links) ----

    member self.GetResourceAsync(decoder: Decoder<'T>, path: string) : Async<Result<HalResource<'T>, string>> =
        async {
            let! result = self.requestAsync HttpMethod.Get path None false
            return result |> Result.bind (Codecs.deserializeHalResource decoder)
        }

    member self.GetCollectionWithLinksAsync(decoder: Decoder<'T>, path: string) : Async<Result<HalCollection<'T>, string>> =
        async {
            let! result = self.requestAsync HttpMethod.Get path None false
            return result |> Result.bind (Codecs.deserializeHalCollection decoder)
        }

    member self.PostResourceAsync(encoder: Encoder<'Req>, decoder: Decoder<'T>, path: string, body: 'Req) : Async<Result<HalResource<'T>, string>> =
        async {
            let json = Codecs.serialize encoder body
            let! result = self.requestAsync HttpMethod.Post path (Some json) false
            return result |> Result.bind (Codecs.deserializeHalResource decoder)
        }

    member self.PostWithoutBodyResourceAsync(decoder: Decoder<'T>, path: string) : Async<Result<HalResource<'T>, string>> =
        async {
            let! result = self.requestAsync HttpMethod.Post path None false
            return result |> Result.bind (Codecs.deserializeHalResource decoder)
        }

    member self.PutResourceAsync(encoder: Encoder<'Req>, decoder: Decoder<'T>, path: string, body: 'Req) : Async<Result<HalResource<'T>, string>> =
        async {
            let json = Codecs.serialize encoder body
            let! result = self.requestAsync HttpMethod.Put path (Some json) false
            return result |> Result.bind (Codecs.deserializeHalResource decoder)
        }

    /// Follow a HATEOAS link and return the full HAL resource with links.
    /// Uses the link's method (defaults to GET if not specified).
    member self.FollowLinkResourceAsync(decoder: Decoder<'T>, link: HalLink, body: (Encoder<'Req>) option, bodyValue: 'Req option) : Async<Result<HalResource<'T>, string>> =
        let method =
            match link.Method with
            | Some m -> HttpMethod.Parse(m)
            | None -> HttpMethod.Get
        async {
            let! result =
                match body, bodyValue with
                | Some enc, Some bv ->
                    let json = Codecs.serialize enc bv
                    self.requestAsync method link.Href (Some json) false
                | _ ->
                    self.requestAsync method link.Href None false
            return result |> Result.bind (Codecs.deserializeHalResource decoder)
        }

    /// Follow a HATEOAS link for a DELETE operation (returns unit).
    member self.FollowDeleteLinkAsync(link: HalLink) : Async<Result<unit, string>> =
        let method =
            match link.Method with
            | Some m -> HttpMethod.Parse(m)
            | None -> HttpMethod.Delete
        async {
            let! result = self.requestAsync method link.Href None false
            return result |> Result.map (fun _ -> ())
        }

    /// Exchange API key + secret for access/refresh tokens (unauthenticated).
    member self.ExchangeTokens(apiKey: string, apiSecret: string) : Async<Result<TokenResponse, string>> =
        async {
            use client = createClient ()
            let request: Domain.TokenExchangeRequest = { ApiKey = apiKey; ApiSecret = apiSecret }
            let body = Codecs.serialize Codecs.tokenExchangeRequestEncoder request
            use content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")

            try
                let! response = client.PostAsync("/api/v1/auth/token", content) |> Async.AwaitTask
                let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                if response.IsSuccessStatusCode then
                    return FoxyBalanceClient.extractData Codecs.tokenResponseDecoder responseBody
                else
                    let msg =
                        match Codecs.deserialize Codecs.apiErrorDecoder responseBody with
                        | Ok apiError -> apiError.Error
                        | Error _ -> $"HTTP {int response.StatusCode}: {responseBody}"
                    return Error msg
            with ex ->
                let rec innerMsg (e: exn) =
                    match e with
                    | :? AggregateException as agg when agg.InnerException <> null -> innerMsg agg.InnerException
                    | :? System.Net.Http.HttpRequestException as hre when hre.InnerException <> null -> innerMsg hre.InnerException
                    | _ -> e.Message
                return Error $"Network error: {innerMsg ex}"
        }

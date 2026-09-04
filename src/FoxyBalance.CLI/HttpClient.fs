namespace FoxyBalance.CLI

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Text
open System.Text.Json
open System.Threading.Tasks
open FoxyBalance.CLI.Domain
open FoxyBalance.CLI.TokenStore

/// HTTP client for the FoxyBalance API with automatic token refresh on 401.
type FoxyBalanceClient(baseUrl: string) =

    let jsonOptions = JsonSerializerOptions.defaults

    let createClient () =
        let client = new HttpClient(HttpHandler.create ())
        client.BaseAddress <- Uri(baseUrl.TrimEnd('/'))
        client.Timeout <- TimeSpan.FromSeconds(30.0)
        client.DefaultRequestHeaders.Accept.Add(MediaTypeWithQualityHeaderValue("application/json"))
        client

    /// Extract the `data` field from a HAL response.
    static member private extractData (body: string) : 'T =
        let hal = JsonSerializer.Deserialize<HalResource<'T>>(body, JsonSerializerOptions.defaults)
        hal.Data

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
                    request.Content <- new StringContent(json, Encoding.UTF8, "application/json")
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
                            try
                                let apiError = JsonSerializer.Deserialize<ApiError>(responseBody, jsonOptions)
                                apiError.Error
                            with _ ->
                                $"HTTP {int response.StatusCode}: {responseBody}"
                        return Error errorMsg
                with ex ->
                    let rec innerMsg (e: exn) =
                        match e with
                        | :? AggregateException as agg when agg.InnerException <> null -> innerMsg agg.InnerException
                        | :? System.Net.Http.HttpRequestException as hre when hre.InnerException <> null -> innerMsg hre.InnerException
                        | _ -> e.Message
                    return Error $"Network error: {innerMsg ex}"
        }

    // ---- Public API methods ----

    member self.GetAsync<'T>(path: string) : Async<Result<'T, string>> =
        async {
            let! result = self.requestAsync HttpMethod.Get path None false
            return result |> Result.map FoxyBalanceClient.extractData<'T>
        }

    member self.GetCollectionAsync<'T>(path: string) : Async<Result<'T list, string>> =
        async {
            let! result = self.requestAsync HttpMethod.Get path None false
            return
                result
                |> Result.map (fun body ->
                    let hal = JsonSerializer.Deserialize<HalCollection<'T>>(body, jsonOptions)
                    hal.Items |> List.map (fun r -> r.Data))
        }

    member self.PostAsync<'T>(path: string, body: obj) : Async<Result<'T, string>> =
        async {
            let json = JsonSerializer.Serialize(body, jsonOptions)
            let! result = self.requestAsync HttpMethod.Post path (Some json) false
            return result |> Result.map FoxyBalanceClient.extractData<'T>
        }

    member self.PostWithoutBodyAsync<'T>(path: string) : Async<Result<'T, string>> =
        async {
            let! result = self.requestAsync HttpMethod.Post path None false
            return result |> Result.map FoxyBalanceClient.extractData<'T>
        }


    member self.PutAsync<'T>(path: string, body: obj) : Async<Result<'T, string>> =
        async {
            let json = JsonSerializer.Serialize(body, jsonOptions)
            let! result = self.requestAsync HttpMethod.Put path (Some json) false
            return result |> Result.map FoxyBalanceClient.extractData<'T>
        }

    member self.DeleteAsync(path: string) : Async<Result<unit, string>> =
        async {
            let! result = self.requestAsync HttpMethod.Delete path None false
            return result |> Result.map (fun _ -> ())
        }

    /// Exchange API key + secret for access/refresh tokens (unauthenticated).
    member self.ExchangeTokens(apiKey: string, apiSecret: string) : Async<Result<TokenResponse, string>> =
        async {
            use client = createClient ()
            let request: Domain.TokenExchangeRequest = { ApiKey = apiKey; ApiSecret = apiSecret }
            let body = JsonSerializer.Serialize(request, jsonOptions)
            use content = new StringContent(body, Encoding.UTF8, "application/json")

            try
                let! response = client.PostAsync("/api/v1/auth/token", content) |> Async.AwaitTask
                let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                if response.IsSuccessStatusCode then
                    return Ok(FoxyBalanceClient.extractData<TokenResponse> responseBody)
                else
                    let msg =
                        try
                            (JsonSerializer.Deserialize<ApiError>(responseBody, jsonOptions)).Error
                        with _ -> $"HTTP {int response.StatusCode}: {responseBody}"
                    return Error msg
            with ex ->
                let rec innerMsg (e: exn) =
                    match e with
                    | :? AggregateException as agg when agg.InnerException <> null -> innerMsg agg.InnerException
                    | :? System.Net.Http.HttpRequestException as hre when hre.InnerException <> null -> innerMsg hre.InnerException
                    | _ -> e.Message
                return Error $"Network error: {innerMsg ex}"
        }

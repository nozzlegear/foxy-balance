namespace FoxyBalance.CLI

open System
open System.Diagnostics
open System.Text
open System.Text.Json
open System.Text.Json.Serialization

/// Token configuration stored in keychain or env vars.
[<CLIMutable>]
type TokenConfig =
    { AccessToken: string
      RefreshToken: string
      BaseUrl: string }

module Keychain =

    let private serviceName = "foxy-balance-cli"

    let internal accountName =
        let user = Environment.UserName
        if String.IsNullOrWhiteSpace user then "default" else user

    /// Run the macOS `security` CLI and return (exitCode, stdout, stderr).
    let private runSecurity (args: string) =
        let psi =
            ProcessStartInfo(
                "security",
                args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            )

        use proc = new Process()
        proc.StartInfo <- psi
        proc.Start() |> ignore

        let stdout = proc.StandardOutput.ReadToEnd()
        let stderr = proc.StandardError.ReadToEnd()
        proc.WaitForExit()
        (proc.ExitCode, stdout.Trim(), stderr.Trim())

    /// Store a generic password in the keychain. Deletes any existing entry first.
    let storePassword (account: string) (value: string) =
        runSecurity $"delete-generic-password -s {serviceName} -a {account}" |> ignore
        let (code, _, err) = runSecurity $"add-generic-password -s {serviceName} -a {account} -w {value} -U"
        if code <> 0 then Error err else Ok ()

    /// Retrieve a generic password from the keychain.
    let retrievePassword (account: string) : string option =
        let (code, stdout, _) = runSecurity $"find-generic-password -s {serviceName} -a {account} -w"
        if code = 0 && not (String.IsNullOrWhiteSpace stdout) then Some stdout else None

    /// Delete a generic password from the keychain.
    let deletePassword (account: string) : Result<unit, string> =
        let (code, _, err) = runSecurity $"delete-generic-password -s {serviceName} -a {account}"
        if code = 0 then Ok () else Error err

module TokenStore =

    /// The account name for storing the access token.
    let private accessTokenAccount = Keychain.accountName + "-access"

    /// The account name for storing the refresh token.
    let private refreshTokenAccount = Keychain.accountName + "-refresh"

    /// The account name for storing the base URL.
    let private baseUrlAccount = Keychain.accountName + "-baseurl"

    /// Default base URL for the FoxyBalance API.
    let defaultBaseUrl = "https://www.foxybalance.com"

    /// Get the base URL from env var, keychain, or default.
    let getBaseUrl () =
        match Environment.GetEnvironmentVariable "FOXY_BALANCE_API_URL" with
        | null
        | "" ->
            match Keychain.retrievePassword baseUrlAccount with
            | Some url -> url
            | None -> defaultBaseUrl
        | url -> url

    /// Save tokens to the keychain.
    let saveTokens (accessToken: string) (refreshToken: string) (baseUrl: string) : Result<unit, string> =
        match Keychain.storePassword accessTokenAccount accessToken with
        | Error e -> Error $"Failed to store access token: {e}"
        | Ok () ->
            match Keychain.storePassword refreshTokenAccount refreshToken with
            | Error e -> Error $"Failed to store refresh token: {e}"
            | Ok () ->
                match Keychain.storePassword baseUrlAccount baseUrl with
                | Error e -> Error $"Failed to store base URL: {e}"
                | Ok () -> Ok ()

    /// Load tokens from keychain, falling back to env vars.
    let loadTokens () : TokenConfig option =
        let accessToken =
            Keychain.retrievePassword accessTokenAccount
            |> Option.orElseWith (fun () ->
                match Environment.GetEnvironmentVariable "FOXY_BALANCE_API_KEY" with
                | null -> None
                | v -> Some v)

        let refreshToken =
            Keychain.retrievePassword refreshTokenAccount
            |> Option.orElseWith (fun () ->
                match Environment.GetEnvironmentVariable "FOXY_BALANCE_REFRESH_TOKEN" with
                | null -> None
                | v -> Some v)

        match accessToken, refreshToken with
        | Some access, Some refresh ->
            Some
                { AccessToken = access
                  RefreshToken = refresh
                  BaseUrl = getBaseUrl () }
        | _ -> None

    /// Clear all stored tokens from the keychain.
    let clearTokens () : Result<unit, string> =
        Keychain.deletePassword accessTokenAccount |> ignore
        Keychain.deletePassword refreshTokenAccount |> ignore
        Keychain.deletePassword baseUrlAccount |> ignore
        Ok ()

    /// Check if tokens are stored.
    let hasTokens () : bool = loadTokens () |> Option.isSome

module TokenRefresh =

    [<CLIMutable>]
    type private TokenResponse =
        { AccessToken: string
          RefreshToken: string
          ExpiresIn: int
          TokenType: string }

    /// Call POST /api/v1/auth/refresh and return the new token config.
    let refresh (refreshToken: string) (baseUrl: string) : Async<Result<TokenConfig, string>> =
        async {
            let url = $"{baseUrl.TrimEnd('/')}/api/v1/auth/refresh"

            let request: Domain.TokenRefreshRequest = { RefreshToken = refreshToken }
            let requestBody = JsonSerializer.Serialize(request, Domain.JsonSerializerOptions.defaults)

            use client = new System.Net.Http.HttpClient(HttpHandler.create ())
            client.Timeout <- TimeSpan.FromSeconds(30.0)

            use content =
                new System.Net.Http.StringContent(requestBody, Text.Encoding.UTF8, "application/json")

            try
                let! response = client.PostAsync(url, content) |> Async.AwaitTask
                let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask

                if not response.IsSuccessStatusCode then
                    return Error $"Token refresh failed ({int response.StatusCode}): {body}"
                else
                    let hal = JsonSerializer.Deserialize<Domain.HalResource<TokenResponse>>(body, Domain.JsonSerializerOptions.defaults)
                    let newConfig =
                        { AccessToken = hal.Data.AccessToken
                          RefreshToken = hal.Data.RefreshToken
                          BaseUrl = baseUrl }

                    match TokenStore.saveTokens newConfig.AccessToken newConfig.RefreshToken newConfig.BaseUrl with
                    | Ok () -> return Ok newConfig
                    | Error e -> return Error $"Token refreshed but failed to save: {e}"
            with ex ->
                let rec innerMsg (e: exn) =
                    match e with
                    | :? AggregateException as agg when agg.InnerException <> null -> innerMsg agg.InnerException
                    | :? System.Net.Http.HttpRequestException as hre when hre.InnerException <> null -> innerMsg hre.InnerException
                    | _ -> e.Message
                return Error $"Network error during token refresh: {innerMsg ex}"
        }

    /// Get a valid access token, refreshing if needed.
    /// If `forceRefresh` is true, always refresh before returning.
    let getValidAccessToken (forceRefresh: bool) : Async<Result<TokenConfig, string>> =
        async {
            match TokenStore.loadTokens () with
            | None -> return Error "Not authenticated. Run 'foxy-balance auth login' first."
            | Some config ->
                if forceRefresh then
                    return! refresh config.RefreshToken config.BaseUrl
                else
                    return Ok config
        }

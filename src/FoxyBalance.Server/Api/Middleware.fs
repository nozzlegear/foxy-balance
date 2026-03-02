namespace FoxyBalance.Server.Api

open System
open Giraffe
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

module Middleware =
    /// Extract Bearer token from Authorization header
    let private extractBearerToken (ctx: HttpContext) : string option =
        match ctx.Request.Headers.TryGetValue("Authorization") with
        | true, values ->
            let authHeader = values.ToString()

            if authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) then
                Some(authHeader.Substring(7))
            else
                None
        | false, _ -> None

    /// Extract API key and secret from headers
    let private extractApiKeyHeaders (ctx: HttpContext) : (string * string) option =
        match ctx.Request.Headers.TryGetValue("X-API-Key"), ctx.Request.Headers.TryGetValue("X-API-Secret") with
        | (true, key), (true, secret) -> Some(key.ToString(), secret.ToString())
        | _ -> None

    /// Try to authenticate via JWT token
    let private tryJwtAuth (ctx: HttpContext) : ApiSession option =
        match extractBearerToken ctx with
        | Some token ->
            let jwtService = ctx.RequestServices.GetRequiredService<JwtService>()

            match jwtService.ValidateAccessToken(token) with
            | Some userId -> Some { UserId = userId; ApiKeyId = None }
            | None -> None
        | None -> None

    /// Try to authenticate via API key headers
    let private tryApiKeyAuth (ctx: HttpContext) : System.Threading.Tasks.Task<ApiSession option> =
        task {
            match extractApiKeyHeaders ctx with
            | Some(apiKey, apiSecret) ->
                let apiKeyService = ctx.RequestServices.GetRequiredService<ApiKeyService>()

                match! apiKeyService.ValidateApiKey(apiKey, apiSecret) with
                | Some(userId, keyId) ->
                    return
                        Some
                            { UserId = userId
                              ApiKeyId = Some keyId }
                | None -> return None
            | None -> return None
        }

    /// JSON error response helper
    let private jsonError (statusCode: int) (message: string) : HttpHandler =
        setStatusCode statusCode >=> json { Error = message; Details = None }

    /// Middleware that requires API authentication (JWT or API key)
    let requiresApiAuthentication: HttpHandler =
        fun next ctx ->
            task {
                // Try JWT first (faster, stateless)
                match tryJwtAuth ctx with
                | Some session ->
                    ctx.Items.["ApiSession"] <- session
                    return! next ctx
                | None ->
                    // Try API key auth
                    match! tryApiKeyAuth ctx with
                    | Some session ->
                        ctx.Items.["ApiSession"] <- session
                        return! next ctx
                    | None ->
                        return!
                            jsonError
                                401
                                "Authentication required. Provide a valid Bearer token or API key headers."
                                earlyReturn
                                ctx
            }

    /// Get the API session from context (use after requiresApiAuthentication)
    let getApiSession (ctx: HttpContext) : ApiSession = ctx.Items.["ApiSession"] :?> ApiSession

    /// Handler wrapper that provides the API session
    let withApiSession (fn: ApiSession -> HttpHandler) : HttpHandler =
        requiresApiAuthentication
        >=> fun next ctx ->
            let session = getApiSession ctx
            fn session next ctx

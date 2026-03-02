namespace FoxyBalance.Server.Api.Routes

open System
open Giraffe
open Microsoft.Extensions.DependencyInjection
open FoxyBalance.Server.Api
open FoxyBalance.Database.Interfaces

module Auth =
    /// POST /api/v1/auth/token
    /// Exchange API key and secret for access and refresh tokens
    let tokenExchangeHandler: HttpHandler =
        fun next ctx ->
            task {
                let! request = ctx.BindJsonAsync<TokenExchangeRequest>()

                if
                    String.IsNullOrWhiteSpace(request.ApiKey)
                    || String.IsNullOrWhiteSpace(request.ApiSecret)
                then
                    return! ApiRouteUtils.validationError "ApiKey and ApiSecret are required" next ctx
                else
                    let apiKeyService = ctx.RequestServices.GetRequiredService<ApiKeyService>()
                    let jwtService = ctx.RequestServices.GetRequiredService<JwtService>()
                    let refreshTokenDb = ctx.RequestServices.GetRequiredService<IRefreshTokenDatabase>()

                    match! apiKeyService.ValidateApiKey(request.ApiKey, request.ApiSecret) with
                    | None -> return! ApiRouteUtils.apiError 401 "Invalid API credentials" next ctx
                    | Some(userId, _keyId) ->
                        // Generate tokens
                        let accessToken = jwtService.GenerateAccessToken(userId)
                        let refreshToken = jwtService.GenerateRefreshToken()
                        let refreshTokenHash = jwtService.HashToken(refreshToken)
                        let expiresAt = jwtService.GetRefreshTokenExpiry()

                        // Store refresh token
                        let! _ = refreshTokenDb.CreateAsync(userId, refreshTokenHash, expiresAt)

                        let response: TokenResponse =
                            { AccessToken = accessToken
                              RefreshToken = refreshToken
                              ExpiresIn = jwtService.GetAccessTokenLifetimeSeconds()
                              TokenType = "Bearer" }

                        let halResponse =
                            HalBuilder.resource
                                response
                                [ LinkRel.Self, HalBuilder.link "/api/v1/auth/token"
                                  LinkRel.TokenRefresh, HalBuilder.linkWithMethod "POST" "/api/v1/auth/refresh"
                                  LinkRel.Balance, HalBuilder.link "/api/v1/balance"
                                  LinkRel.Transactions, HalBuilder.link "/api/v1/transactions"
                                  LinkRel.Bills, HalBuilder.link "/api/v1/bills" ]

                        return! ApiRouteUtils.halJson halResponse next ctx
            }

    /// POST /api/v1/auth/refresh
    /// Exchange a refresh token for new access and refresh tokens
    let tokenRefreshHandler: HttpHandler =
        fun next ctx ->
            task {
                let! request = ctx.BindJsonAsync<TokenRefreshRequest>()

                if String.IsNullOrWhiteSpace(request.RefreshToken) then
                    return! ApiRouteUtils.validationError "RefreshToken is required" next ctx
                else
                    let jwtService = ctx.RequestServices.GetRequiredService<JwtService>()
                    let refreshTokenDb = ctx.RequestServices.GetRequiredService<IRefreshTokenDatabase>()

                    let tokenHash = jwtService.HashToken(request.RefreshToken)

                    // Atomically consume the refresh token - prevents race conditions
                    // Returns None if token doesn't exist, is already used, or is expired
                    match! refreshTokenDb.ConsumeRefreshTokenAsync(tokenHash) with
                    | None ->
                        // Use generic error message to prevent information disclosure
                        return! ApiRouteUtils.apiError 401 "Invalid or expired refresh token" next ctx
                    | Some tokenInfo ->
                        // Generate new token pair
                        let newAccessToken = jwtService.GenerateAccessToken(tokenInfo.UserId)
                        let newRefreshToken = jwtService.GenerateRefreshToken()
                        let newRefreshTokenHash = jwtService.HashToken(newRefreshToken)
                        let newExpiresAt = jwtService.GetRefreshTokenExpiry()

                        // Store new refresh token
                        let! _ = refreshTokenDb.CreateAsync(tokenInfo.UserId, newRefreshTokenHash, newExpiresAt)

                        let response: TokenResponse =
                            { AccessToken = newAccessToken
                              RefreshToken = newRefreshToken
                              ExpiresIn = jwtService.GetAccessTokenLifetimeSeconds()
                              TokenType = "Bearer" }

                        let halResponse =
                            HalBuilder.resource
                                response
                                [ LinkRel.Self, HalBuilder.link "/api/v1/auth/refresh"
                                  LinkRel.TokenRefresh, HalBuilder.linkWithMethod "POST" "/api/v1/auth/refresh"
                                  LinkRel.Balance, HalBuilder.link "/api/v1/balance" ]

                        return! ApiRouteUtils.halJson halResponse next ctx
            }

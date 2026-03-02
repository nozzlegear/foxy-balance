namespace FoxyBalance.Server.Routes

open Giraffe
open FoxyBalance.Server
open FoxyBalance.Server.Api
open FoxyBalance.Server.Models.ViewModels
open Microsoft.Extensions.DependencyInjection

module Views = FoxyBalance.Server.Views.ApiKeys

module ApiKeys =
    let listApiKeysHandler : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let apiKeyService = ctx.GetService<ApiKeyService>()
            let! apiKeys = apiKeyService.ListKeysForUser(session.UserId)

            let model : ApiKeysListViewModel =
                { ApiKeys = apiKeys }

            return! (Views.listApiKeysPage model |> htmlView) next ctx
        })

    let newApiKeyHandler : HttpHandler =
        NewApiKeyViewModel.Default
        |> Views.newApiKeyPage
        |> htmlView

    let newApiKeyPostHandler : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let! request = ctx.BindFormAsync<Models.RequestModels.CreateApiKeyRequest>()
            let apiKeyService = ctx.GetService<ApiKeyService>()

            match Models.RequestModels.CreateApiKeyRequest.Validate request with
            | Error msg ->
                let model =
                    { Error = Some msg
                      Name = request.Name }

                let view =
                    model
                    |> Views.newApiKeyPage
                    |> htmlView
                    >=> setStatusCode 422

                return! view next ctx
            | Ok validatedName ->
                let! (keyId, apiKey, apiSecret) = apiKeyService.CreateApiKeyPair(session.UserId, validatedName)

                // Get the base URL from the request for displaying in the help text
                let baseUrl = sprintf "%s://%s" ctx.Request.Scheme (ctx.Request.Host.ToString())

                let model : ApiKeyCreatedViewModel =
                    { Name = validatedName
                      ApiKey = apiKey
                      ApiSecret = apiSecret
                      BaseUrl = baseUrl }

                return! (Views.apiKeyCreatedPage model |> htmlView) next ctx
        })

    let revokeApiKeyPostHandler (keyId: int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let apiKeyService = ctx.GetService<ApiKeyService>()
            do! apiKeyService.RevokeKey(session.UserId, keyId)
            return! redirectTo false "/api-keys" next ctx
        })

    let deleteApiKeyPostHandler (keyId: int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let apiKeyService = ctx.GetService<ApiKeyService>()
            do! apiKeyService.DeleteKey(session.UserId, keyId)
            return! redirectTo false "/api-keys" next ctx
        })

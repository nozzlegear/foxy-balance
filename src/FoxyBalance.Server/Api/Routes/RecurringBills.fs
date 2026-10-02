namespace FoxyBalance.Server.Api.Routes

open System
open Giraffe
open Microsoft.Extensions.DependencyInjection
open FoxyBalance.Server.Api
open FoxyBalance.Server.Api.Domain
open FoxyBalance.Server.Api.Requests
open FoxyBalance.Server.Api.Responses
open FoxyBalance.Server.Models.RequestModels
open FoxyBalance.Database.Interfaces

module RecurringTransactions =
    let private toEditTransactionRequest (req: ApiRecurringTransactionRequest) : EditRecurringTransactionRequest =
        { Name = req.Name
          Amount = req.Amount
          WeekOfMonth = req.WeekOfMonth
          DayOfWeek = req.DayOfWeek }

    /// GET /api/v1/bills  (recurring transactions)
    let listHandler: HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let activeOnly =
                    ctx.TryGetQueryStringValue "active"
                    |> Option.map (fun v -> v.ToLowerInvariant() = "true")
                    |> Option.defaultValue false

                let recTxDb = ctx.RequestServices.GetRequiredService<IRecurringTransactionDatabase>()
                let! recurringTransactions = recTxDb.ListAsync(session.UserId, activeOnly)

                let rtList = recurringTransactions |> Seq.toList

                let itemLinks =
                    rtList
                    |> List.map (fun t ->
                        HalBuilder.resource (ApiDtos.fromRecurringTransaction t) (HalBuilder.recurringTransactionLinks t.Id))

                let activeQuery = if activeOnly then "&active=true" else ""

                let collectionLinks =
                    [ LinkRel.Self, HalBuilder.link $"/api/v1/bills?active={activeOnly}"
                      LinkRel.Create, HalBuilder.linkWithMethod "POST" "/api/v1/bills"
                      LinkRel.MatchSuggestions, HalBuilder.link "/api/v1/bills/match/suggestions"
                      LinkRel.Balance, HalBuilder.link "/api/v1/balance" ]

                let response =
                    HalBuilder.collection itemLinks 1 1 (List.length rtList) collectionLinks

                return! ApiRouteUtils.halJson response next ctx
            })

    /// GET /api/v1/bills/{id}
    let getHandler (recurringTransactionId: int64) : HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let recTxDb = ctx.RequestServices.GetRequiredService<IRecurringTransactionDatabase>()

                match! recTxDb.GetAsync(session.UserId, recurringTransactionId) with
                | None -> return! ApiRouteUtils.notFound "Recurring transaction" next ctx
                | Some t ->
                    let halResponse =
                        HalBuilder.resource (ApiDtos.fromRecurringTransaction t) (HalBuilder.recurringTransactionLinks recurringTransactionId)

                    return! ApiRouteUtils.halJson halResponse next ctx
            })

    /// POST /api/v1/bills
    let createHandler: HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let! request = ctx.BindJsonAsync<ApiRecurringTransactionRequest>()

                match EditRecurringTransactionRequest.Validate(toEditTransactionRequest request) with
                | Error msg -> return! ApiRouteUtils.validationError msg next ctx
                | Ok partialTransaction ->
                    let recTxDb = ctx.RequestServices.GetRequiredService<IRecurringTransactionDatabase>()
                    let! created = recTxDb.CreateAsync(session.UserId, partialTransaction)

                    let halResponse =
                        HalBuilder.resource (ApiDtos.fromRecurringTransaction created) (HalBuilder.recurringTransactionLinks created.Id)

                    return! ApiRouteUtils.created halResponse next ctx
            })

    /// PUT /api/v1/bills/{id}
    let updateHandler (recurringTransactionId: int64) : HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let recTxDb = ctx.RequestServices.GetRequiredService<IRecurringTransactionDatabase>()

                match! recTxDb.GetAsync(session.UserId, recurringTransactionId) with
                | None -> return! ApiRouteUtils.notFound "Recurring transaction" next ctx
                | Some _ ->
                    let! request = ctx.BindJsonAsync<ApiRecurringTransactionRequest>()

                    match EditRecurringTransactionRequest.Validate(toEditTransactionRequest request) with
                    | Error msg -> return! ApiRouteUtils.validationError msg next ctx
                    | Ok partialTransaction ->
                        let! updated = recTxDb.UpdateAsync(session.UserId, recurringTransactionId, partialTransaction)

                        let halResponse =
                            HalBuilder.resource (ApiDtos.fromRecurringTransaction updated) (HalBuilder.recurringTransactionLinks recurringTransactionId)

                        return! ApiRouteUtils.halJson halResponse next ctx
            })

    /// DELETE /api/v1/bills/{id}
    let deleteHandler (recurringTransactionId: int64) : HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let recTxDb = ctx.RequestServices.GetRequiredService<IRecurringTransactionDatabase>()

                match! recTxDb.GetAsync(session.UserId, recurringTransactionId) with
                | None -> return! ApiRouteUtils.notFound "Recurring transaction" next ctx
                | Some _ ->
                    do! recTxDb.DeleteAsync(session.UserId, recurringTransactionId)
                    return! ApiRouteUtils.noContent next ctx
            })

    /// POST /api/v1/bills/{id}/toggle-active
    let toggleActiveHandler (recurringTransactionId: int64) : HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let recTxDb = ctx.RequestServices.GetRequiredService<IRecurringTransactionDatabase>()

                match! recTxDb.GetAsync(session.UserId, recurringTransactionId) with
                | None -> return! ApiRouteUtils.notFound "Recurring transaction" next ctx
                | Some t ->
                    do! recTxDb.SetActiveAsync(session.UserId, recurringTransactionId, not t.Active)
                    let! updated = recTxDb.GetAsync(session.UserId, recurringTransactionId)

                    match updated with
                    | Some updatedT ->
                        let halResponse =
                            HalBuilder.resource (ApiDtos.fromRecurringTransaction updatedT) (HalBuilder.recurringTransactionLinks recurringTransactionId)

                        return! ApiRouteUtils.halJson halResponse next ctx
                    | None -> return! ApiRouteUtils.notFound "Recurring transaction" next ctx
            })

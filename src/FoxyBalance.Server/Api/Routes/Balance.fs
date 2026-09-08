namespace FoxyBalance.Server.Api.Routes

open System
open Giraffe
open Microsoft.Extensions.DependencyInjection
open FoxyBalance.Server.Api
open FoxyBalance.Server.Api.Domain
open FoxyBalance.Database.Interfaces

module Balance =
    /// GET /api/v1/balance
    /// Get the user's balance summary
    let getBalanceHandler: HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let transactionDb = ctx.RequestServices.GetRequiredService<ITransactionDatabase>()
                let! sum = transactionDb.SumAsync(session.UserId)

                let halResponse = HalBuilder.resource sum (HalBuilder.balanceLinks ())

                return! ApiRouteUtils.halJson halResponse next ctx
            })

    /// GET /api/v1/balance/as-of-date?date=yyyy-MM-dd&includePending=true|false
    /// Get the user's balance as of a specific date (exclusive of that date's transactions)
    let getBalanceAsOfDateHandler: HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                match ctx.TryGetQueryStringValue "date" with
                | None ->
                    return! ApiRouteUtils.validationError "date query parameter is required (format: yyyy-MM-dd)" next ctx
                | Some dateStr ->
                    match DateTimeOffset.TryParse(dateStr) with
                    | false, _ ->
                        return! ApiRouteUtils.validationError "Invalid date format. Use yyyy-MM-dd." next ctx
                    | true, date ->
                        let includePending =
                            ctx.TryGetQueryStringValue "includePending"
                            |> Option.bind (fun s ->
                                match Boolean.TryParse(s) with
                                | true, v -> Some v
                                | _ -> None)
                            |> Option.defaultValue false

                        let transactionDb = ctx.RequestServices.GetRequiredService<ITransactionDatabase>()
                        let! sum = transactionDb.SumAsOfDateAsync(session.UserId, date, includePending)

                        let halResponse = HalBuilder.resource sum (HalBuilder.balanceAsOfDateLinks dateStr includePending)

                        return! ApiRouteUtils.halJson halResponse next ctx
            })

    /// GET /api/v1/balance/before-transaction/{id}?useClearDate=true|false
    /// Get the user's balance before a specific transaction (by creation date or clear date)
    let getBalanceBeforeTransactionHandler (transactionId: int64) : HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let useClearDate =
                    ctx.TryGetQueryStringValue "useClearDate"
                    |> Option.bind (fun s ->
                        match Boolean.TryParse(s) with
                        | true, v -> Some v
                        | _ -> None)
                    |> Option.defaultValue false

                let transactionDb = ctx.RequestServices.GetRequiredService<ITransactionDatabase>()

                // Check transaction exists first for proper 404
                let! exists = transactionDb.ExistsAsync(session.UserId, transactionId)
                if not exists then
                    return! ApiRouteUtils.notFound "Transaction" next ctx
                else
                    let! sum = transactionDb.SumBeforeTransactionAsync(session.UserId, transactionId, useClearDate)

                    let halResponse = HalBuilder.resource sum (HalBuilder.balanceBeforeTransactionLinks transactionId useClearDate)

                    return! ApiRouteUtils.halJson halResponse next ctx
            })

    /// GET /api/v1/balance/after-transaction/{id}?useClearDate=true|false
    /// Get the user's balance after a specific transaction (by creation date or clear date)
    let getBalanceAfterTransactionHandler (transactionId: int64) : HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let useClearDate =
                    ctx.TryGetQueryStringValue "useClearDate"
                    |> Option.bind (fun s ->
                        match Boolean.TryParse(s) with
                        | true, v -> Some v
                        | _ -> None)
                    |> Option.defaultValue false

                let transactionDb = ctx.RequestServices.GetRequiredService<ITransactionDatabase>()

                let! exists = transactionDb.ExistsAsync(session.UserId, transactionId)
                if not exists then
                    return! ApiRouteUtils.notFound "Transaction" next ctx
                else
                    let! sum = transactionDb.SumAfterTransactionAsync(session.UserId, transactionId, useClearDate)

                    let halResponse = HalBuilder.resource sum (HalBuilder.balanceAfterTransactionLinks transactionId useClearDate)

                    return! ApiRouteUtils.halJson halResponse next ctx
            })

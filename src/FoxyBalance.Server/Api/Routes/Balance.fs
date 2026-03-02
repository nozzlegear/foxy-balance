namespace FoxyBalance.Server.Api.Routes

open Giraffe
open Microsoft.Extensions.DependencyInjection
open FoxyBalance.Server.Api
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

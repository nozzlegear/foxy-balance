namespace FoxyBalance.Server.Api.Routes

open Giraffe
open Microsoft.Extensions.DependencyInjection
open FoxyBalance.Server.Api
open FoxyBalance.Server.Services
open FoxyBalance.Database.Interfaces

module BillMatching =
    /// GET /api/v1/bills/match/suggestions
    /// Get match suggestions for transactions and bills
    let getSuggestionsHandler: HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let matchingService = ctx.RequestServices.GetRequiredService<BillMatchingService>()
                let! suggestions = matchingService.GetMatchSuggestionsForUser(session.UserId)

                let suggestionItems =
                    suggestions
                    |> List.map (fun s ->
                        // Convert to DTOs for serialization
                        let transactionDto = ApiDtos.fromTransaction s.Transaction

                        let billDto =
                            {| Id = s.RecurringBill.Id
                               Name = s.RecurringBill.Name
                               Amount = s.RecurringBill.Amount
                               WeekOfMonth = s.RecurringBill.WeekOfMonth.ToInt()
                               DayOfWeek = int s.RecurringBill.DayOfWeek
                               Active = s.RecurringBill.Active |}

                        let data =
                            {| Transaction = transactionDto
                               RecurringBill = billDto
                               MatchScore = s.MatchScore |}

                        HalBuilder.resource
                            data
                            [ LinkRel.ExecuteMatch, HalBuilder.linkWithMethod "POST" "/api/v1/bills/match"
                              LinkRel.Transaction s.Transaction.Id,
                              HalBuilder.link $"/api/v1/transactions/{s.Transaction.Id}"
                              LinkRel.Bill s.RecurringBill.Id, HalBuilder.link $"/api/v1/bills/{s.RecurringBill.Id}" ])

                let collectionLinks =
                    [ LinkRel.Self, HalBuilder.link "/api/v1/bills/match/suggestions"
                      LinkRel.ExecuteMatch, HalBuilder.linkWithMethod "POST" "/api/v1/bills/match"
                      LinkRel.Bills, HalBuilder.link "/api/v1/bills"
                      LinkRel.Transactions, HalBuilder.link "/api/v1/transactions" ]

                let response =
                    HalBuilder.collection suggestionItems 1 1 (List.length suggestions) collectionLinks

                return! ApiRouteUtils.halJson response next ctx
            })

    /// POST /api/v1/bills/match
    /// Execute a match between a transaction and a bill
    let executeMatchHandler: HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let! request = ctx.BindJsonAsync<ApiMatchRequest>()

                if request.TransactionId <= 0L then
                    return! ApiRouteUtils.validationError "TransactionId is required" next ctx
                elif request.BillId <= 0L then
                    return! ApiRouteUtils.validationError "BillId is required" next ctx
                else
                    // Verify the bill belongs to this user
                    let billDb = ctx.RequestServices.GetRequiredService<IRecurringBillDatabase>()

                    match! billDb.GetAsync(session.UserId, request.BillId) with
                    | None -> return! ApiRouteUtils.notFound "Bill" next ctx
                    | Some _ ->
                        let matchingService = ctx.RequestServices.GetRequiredService<BillMatchingService>()

                        match!
                            matchingService.MatchTransactionToBill(
                                session.UserId,
                                request.TransactionId,
                                request.BillId
                            )
                        with
                        | Error msg when msg.Contains("not found", System.StringComparison.OrdinalIgnoreCase) ->
                            return! ApiRouteUtils.notFound "Transaction" next ctx
                        | Error msg -> return! ApiRouteUtils.validationError msg next ctx
                        | Ok transaction ->
                            // Convert to DTO for serialization
                            let transactionDto = ApiDtos.fromTransaction transaction

                            let halResponse =
                                HalBuilder.resource
                                    transactionDto
                                    [ LinkRel.Self, HalBuilder.link $"/api/v1/transactions/{transaction.Id}"
                                      LinkRel.Bill request.BillId, HalBuilder.link $"/api/v1/bills/{request.BillId}"
                                      LinkRel.MatchSuggestions, HalBuilder.link "/api/v1/bills/match/suggestions" ]

                            return! ApiRouteUtils.halJson halResponse next ctx
            })

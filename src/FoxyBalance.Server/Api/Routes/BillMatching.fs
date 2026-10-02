namespace FoxyBalance.Server.Api.Routes

open Giraffe
open Microsoft.Extensions.DependencyInjection
open FoxyBalance.Server.Api
open FoxyBalance.Server.Api.Domain
open FoxyBalance.Server.Api.Requests
open FoxyBalance.Server.Api.Responses
open FoxyBalance.Server.Services
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
module BillMatching =
    /// GET /api/v1/bills/match/suggestions
    let getSuggestionsHandler: HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let matchingService = ctx.RequestServices.GetRequiredService<RecurringTransactionMatchingService>()
                let! suggestions = matchingService.GetMatchSuggestionsForUser(session.UserId)

                let suggestionItems =
                    suggestions
                    |> List.map (fun s ->
                        // Convert to DTOs for serialization
                        let transactionDto = ApiDtos.fromTransaction s.Transaction

                        let rt = s.RecurringTransaction
                        let scheduleInfo =
                            match rt.Schedule with
                            | ScheduleType.ByWeekOfMonth sched -> (sched.WeekOfMonth.ToInt(), int sched.DayOfWeek)
                            | ScheduleType.ByCalendarDate sched -> (0, sched.Date)

                        let rtDto =
                            {| Id = rt.Id
                               Name = rt.Name
                               Amount = rt.Amount
                               WeekOfMonth = fst scheduleInfo
                               DayOfWeek = snd scheduleInfo
                               Active = rt.Active |}

                        let data =
                            {| Transaction = transactionDto
                               RecurringTransaction = rtDto
                               MatchScore = s.MatchScore |}

                        HalBuilder.resource
                            data
                            [ LinkRel.ExecuteMatch, HalBuilder.linkWithMethod "POST" "/api/v1/bills/match"
                              LinkRel.Transaction s.Transaction.Id,
                              HalBuilder.link $"/api/v1/transactions/{s.Transaction.Id}"
                              LinkRel.RecurringTransaction s.RecurringTransaction.Id, HalBuilder.link $"/api/v1/bills/{s.RecurringTransaction.Id}" ])

                let collectionLinks =
                    [ LinkRel.Self, HalBuilder.link "/api/v1/bills/match/suggestions"
                      LinkRel.ExecuteMatch, HalBuilder.linkWithMethod "POST" "/api/v1/bills/match"
                      LinkRel.RecurringTransactions, HalBuilder.link "/api/v1/bills"
                      LinkRel.Transactions, HalBuilder.link "/api/v1/transactions" ]

                let response =
                    HalBuilder.collection suggestionItems 1 1 (List.length suggestions) collectionLinks

                return! ApiRouteUtils.halJson response next ctx
            })

    /// POST /api/v1/bills/match
    let executeMatchHandler: HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let! request = ctx.BindJsonAsync<ApiMatchRequest>()

                if request.TransactionId <= 0L then
                    return! ApiRouteUtils.validationError "TransactionId is required" next ctx
                elif request.RecurringTransactionId <= 0L then
                    return! ApiRouteUtils.validationError "RecurringTransactionId is required" next ctx
                else
                    // Verify the recurring transaction belongs to this user
                    let recTxDb = ctx.RequestServices.GetRequiredService<IRecurringTransactionDatabase>()

                    match! recTxDb.GetAsync(session.UserId, request.RecurringTransactionId) with
                    | None -> return! ApiRouteUtils.notFound "Recurring transaction" next ctx
                    | Some _ ->
                        let matchingService = ctx.RequestServices.GetRequiredService<RecurringTransactionMatchingService>()

                        match!
                            matchingService.MatchTransactionToRecurringTransaction(
                                session.UserId,
                                request.TransactionId,
                                request.RecurringTransactionId
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
                                      LinkRel.RecurringTransaction request.RecurringTransactionId, HalBuilder.link $"/api/v1/bills/{request.RecurringTransactionId}"
                                      LinkRel.MatchSuggestions, HalBuilder.link "/api/v1/bills/match/suggestions" ]

                            return! ApiRouteUtils.halJson halResponse next ctx
            })

namespace FoxyBalance.Server.Routes

open FoxyBalance.Database.Interfaces
open FoxyBalance.Server.Services
open Giraffe
open FoxyBalance.Server
open FoxyBalance.Database.Models
open FoxyBalance.Server.Models.RequestModels
open FoxyBalance.Server.Models.ViewModels
open Microsoft.Extensions.DependencyInjection

module Views = FoxyBalance.Server.Views.Bills

module Bills =
    let listBillsHandler : HttpHandler =
        RouteUtils.withSession(fun session next ctx -> task {
            let database = ctx.GetService<IRecurringTransactionDatabase>()
            let! recurringTransactions = database.ListAsync(session.UserId, false)

            let model : RecurringBillsListViewModel =
                { Bills = recurringTransactions }

            return! (Views.listBillsPage model |> htmlView) next ctx
        })

    let newBillHandler : HttpHandler =
        EditRecurringBillViewModel.Default
        |> NewBill
        |> Views.createOrEditBillPage
        |> htmlView

    let editBillHandler (billId : int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<IRecurringTransactionDatabase>()

            match! database.GetAsync(session.UserId, billId) with
            | Some t ->
                let view =
                    (billId, EditRecurringBillViewModel.FromExistingTransaction t)
                    |> ExistingBill
                    |> Views.createOrEditBillPage
                    |> htmlView
                return! view next ctx
            | None ->
                return! (setStatusCode 404 >=> text "Not Found") next ctx
        })

    let newBillPostHandler : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let! request = ctx.BindFormAsync<EditRecurringTransactionRequest>()
            let database = ctx.GetService<IRecurringTransactionDatabase>()

            match EditRecurringTransactionRequest.Validate request with
            | Error msg ->
                let model =
                    { Error = Some msg
                      Name = request.Name
                      Amount = request.Amount
                      WeekOfMonth = request.WeekOfMonth |> Option.defaultValue "1"
                      DayOfWeek = request.DayOfWeek |> Option.defaultValue "0"
                      Type = request.Type |> Option.defaultValue "bill" }

                let view =
                    model
                    |> NewBill
                    |> Views.createOrEditBillPage
                    |> htmlView
                    >=> setStatusCode 422

                return! view next ctx
            | Ok partialTransaction ->
                let! _created = database.CreateAsync(session.UserId, partialTransaction)
                return! redirectTo false "/recurring" next ctx
        })

    let existingBillPostHandler (billId : int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let! request = ctx.BindFormAsync<EditRecurringTransactionRequest>()
            let database = ctx.GetService<IRecurringTransactionDatabase>()

            match EditRecurringTransactionRequest.Validate request with
            | Error msg ->
                let model =
                    { Error = Some msg
                      Name = request.Name
                      Amount = request.Amount
                      WeekOfMonth = request.WeekOfMonth |> Option.defaultValue "1"
                      DayOfWeek = request.DayOfWeek |> Option.defaultValue "0"
                      Type = request.Type |> Option.defaultValue "bill" }

                let view =
                    (billId, model)
                    |> ExistingBill
                    |> Views.createOrEditBillPage
                    |> htmlView
                    >=> setStatusCode 422

                return! view next ctx
            | Ok partialTransaction ->
                let! _updated = database.UpdateAsync(session.UserId, billId, partialTransaction)
                return! redirectTo false "/recurring" next ctx
        })

    let deleteBillPostHandler (billId : int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<IRecurringTransactionDatabase>()
            do! database.DeleteAsync(session.UserId, billId)
            return! redirectTo false "/recurring" next ctx
        })

    let toggleActiveBillPostHandler (billId : int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<IRecurringTransactionDatabase>()

            // Get the current transaction to check its active status
            match! database.GetAsync(session.UserId, billId) with
            | Some t ->
                // Toggle the active status
                do! database.SetActiveAsync(session.UserId, billId, not t.Active)
                return! redirectTo false "/recurring" next ctx
            | None ->
                return! (setStatusCode 404 >=> text "Not found") next ctx
        })

    let matchingInterfaceHandler : HttpHandler =
        RouteUtils.withSession(fun session next ctx -> task {
            let matchingService = ctx.GetService<RecurringTransactionMatchingService>()
            let! suggestions = matchingService.GetMatchSuggestionsForUser(session.UserId)

            let model : BillMatchingViewModel =
                { MatchCandidates = suggestions }

            return! (Views.matchingPage model |> htmlView) next ctx
        })

    let executeMatchHandler (transactionId : int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let! request = ctx.BindFormAsync<MatchTransactionRequest>()
            let matchingService = ctx.GetService<RecurringTransactionMatchingService>()

            let! result = matchingService.MatchTransactionToRecurringTransaction(
                session.UserId,
                transactionId,
                request.RecurringTransactionId)

            match result with
            | Ok _ ->
                return! redirectTo false "/recurring/match" next ctx
            | Error msg ->
                return! (setStatusCode 422 >=> text msg) next ctx
        })

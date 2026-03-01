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
            let database = ctx.GetService<IRecurringBillDatabase>()
            let! bills = database.ListAsync(session.UserId, false)

            let model : RecurringBillsListViewModel =
                { Bills = bills }

            return! (Views.listBillsPage model |> htmlView) next ctx
        })

    let newBillHandler : HttpHandler =
        EditRecurringBillViewModel.Default
        |> NewBill
        |> Views.createOrEditBillPage
        |> htmlView

    let editBillHandler (billId : int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<IRecurringBillDatabase>()

            match! database.GetAsync(session.UserId, billId) with
            | Some bill ->
                let view =
                    (billId, EditRecurringBillViewModel.FromExistingBill bill)
                    |> ExistingBill
                    |> Views.createOrEditBillPage
                    |> htmlView
                return! view next ctx
            | None ->
                return! (setStatusCode 404 >=> text "Not Found") next ctx
        })

    let newBillPostHandler : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let! request = ctx.BindFormAsync<EditRecurringBillRequest>()
            let database = ctx.GetService<IRecurringBillDatabase>()

            match EditRecurringBillRequest.Validate request with
            | Error msg ->
                let model =
                    { Error = Some msg
                      Name = request.Name
                      Amount = request.Amount
                      WeekOfMonth = request.WeekOfMonth
                      DayOfWeek = request.DayOfWeek }

                let view =
                    model
                    |> NewBill
                    |> Views.createOrEditBillPage
                    |> htmlView
                    >=> setStatusCode 422

                return! view next ctx
            | Ok partialBill ->
                let! _createdBill = database.CreateAsync(session.UserId, partialBill)
                return! redirectTo false "/bills" next ctx
        })

    let existingBillPostHandler (billId : int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let! request = ctx.BindFormAsync<EditRecurringBillRequest>()
            let database = ctx.GetService<IRecurringBillDatabase>()

            match EditRecurringBillRequest.Validate request with
            | Error msg ->
                let model =
                    { Error = Some msg
                      Name = request.Name
                      Amount = request.Amount
                      WeekOfMonth = request.WeekOfMonth
                      DayOfWeek = request.DayOfWeek }

                let view =
                    (billId, model)
                    |> ExistingBill
                    |> Views.createOrEditBillPage
                    |> htmlView
                    >=> setStatusCode 422

                return! view next ctx
            | Ok partialBill ->
                let! _updatedBill = database.UpdateAsync(session.UserId, billId, partialBill)
                return! redirectTo false "/bills" next ctx
        })

    let deleteBillPostHandler (billId : int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<IRecurringBillDatabase>()
            do! database.DeleteAsync(session.UserId, billId)
            return! redirectTo false "/bills" next ctx
        })

    let toggleActiveBillPostHandler (billId : int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<IRecurringBillDatabase>()

            // Get the current bill to check its active status
            match! database.GetAsync(session.UserId, billId) with
            | Some bill ->
                // Toggle the active status
                do! database.SetActiveAsync(session.UserId, billId, not bill.Active)
                return! redirectTo false "/bills" next ctx
            | None ->
                return! (setStatusCode 404 >=> text "Bill not found") next ctx
        })

    let matchingInterfaceHandler : HttpHandler =
        RouteUtils.withSession(fun session next ctx -> task {
            let matchingService = ctx.GetService<BillMatchingService>()
            let! suggestions = matchingService.GetMatchSuggestionsForUser(session.UserId)

            let model : BillMatchingViewModel =
                { MatchCandidates = suggestions }

            return! (Views.matchingPage model |> htmlView) next ctx
        })

    let executeMatchHandler (transactionId : int64) : HttpHandler =
        RouteUtils.withSession(fun session next ctx -> task {
            let! request = ctx.BindFormAsync<MatchTransactionRequest>()
            let matchingService = ctx.GetService<BillMatchingService>()

            let! result = matchingService.MatchTransactionToBill(
                session.UserId,
                transactionId,
                request.BillId)

            match result with
            | Ok _ ->
                return! redirectTo false "/bills/match" next ctx
            | Error msg ->
                return! (setStatusCode 422 >=> text msg) next ctx
        })

namespace FoxyBalance.Server.Controllers

open System
open System.Threading.Tasks
open FoxyBalance.Database.Interfaces
open FoxyBalance.Server
open FoxyBalance.Database.Models
open FoxyBalance.Server.Authentication
open FoxyBalance.BlazorViews
open FoxyBalance.Server.Models.RequestModels
open FoxyBalance.Server.Models.ViewModels
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open System.ComponentModel.DataAnnotations

module Views = FoxyBalance.Server.Views.Balance

[<Route("balance"); Authorize>]
type BalanceController(
    sessionUtil: ISessionLoaderUtil,
    transactionDatabase: ITransactionDatabase
) =
    inherit Controller()

    [<HttpGet("")>]
    member this.GetBalanceView(
        [<FromQuery; Range(1, Int32.MaxValue)>] ?page: int,
        [<FromQuery; AllowedValues("pending", "cleared")>] ?status: string
    ): Task<ExtendedActionResult> =
        // TODO: have asp.net auto-validate querystring instead of checking model validation state manually
        let page = if not this.ModelState.IsValid
                   then 1
                   else defaultArg page 1
        let session = sessionUtil.LoadSessionFromPrincipal(this.User)
        let recordsPerPage = 35
        let status = match Option.map String.toLower status with
                     | Some "pending" -> PendingTransactions
                     | Some "cleared" -> ClearedTransactions
                     | _ -> AllTransactions

        task {
            let! transactions =
                { Limit = recordsPerPage
                  Offset = recordsPerPage * (page - 1)
                  Order = Descending
                  Status = status }
                |> transactionDatabase.ListAsync session.UserId
            let! count = transactionDatabase.CountAsync session.UserId status
            let! sum = transactionDatabase.SumAsync session.UserId
            let model : HomePageViewModel =
                { Transactions = transactions
                  Sum = sum
                  Page = page
                  Status = status
                  TotalPages = if count % recordsPerPage > 0 then (count / recordsPerPage) + 1 else count / recordsPerPage
                  TotalTransactions = count }

            return Blazor.Component<Components.Pages.Home>
                   |> Blazor.SetParameters [
                       "model" => model
                   ]
                   |> ExtendedActionResult.BlazorView
            // return! htmlView (Views.homePage model) next ctx
        }

    [<HttpGet("new")>]
    member this.GetCreateTransactionPage(): ExtendedActionResult =
        Blazor.Component<Components.Pages.Home>
        |> Blazor.SetParameters [
            "model" => NewTransaction EditTransactionViewModel.Default
        ]
        |> ExtendedActionResult.BlazorView
        // EditTransactionViewModel.Default
        // |> NewTransaction
        // |> Views.createOrEditTransactionPage
        // |> htmlView

    [<HttpGet("{transactionId}")>]
    member this.GetEditTransactionPage(
        [<FromRoute; Required>] transactionId: int64
    ): Task<ExtendedActionResult> =
        let session = sessionUtil.LoadSessionFromPrincipal(this.User)
        task {
            match! transactionDatabase.GetAsync session.UserId transactionId with
            | Some transaction ->
                return Blazor.Component<Components.Pages.Home>
                       |> Blazor.SetParameters [
                           "model" => ExistingTransaction (transactionId, EditTransactionViewModel.FromExistingTransaction transaction)
                       ]
                       |> ExtendedActionResult.BlazorView
                    // (transactionId, EditTransactionViewModel.FromExistingTransaction transaction)
                    // |> ExistingTransaction
                    // |> Views.createOrEditTransactionPage
                    // |> htmlView
            | None ->
                return ExtendedActionResult.ErrorView (404, $"Could not find a transaction with id {transactionId}.")
        }

    [<NonAction>]
    member private this.CreateOrEditTransaction(
        transactionId: int64 option,
        request: EditTransactionRequest
    ): Task<ExtendedActionResult> =
        task {
            // TODO: use asp.net model binding validation here, then do a simple map to the transaction model from there
            match EditTransactionRequest.Validate request with
            | Error msg ->
                this.ModelState.AddModelError("", msg)

                let viewModel = EditTransactionViewModel.FromBadRequest request msg
                let viewModel =
                    transactionId
                    |> Option.map (fun i -> ExistingTransaction (i, viewModel))
                    |> Option.defaultWith (fun _ -> NewTransaction viewModel)
                    |> Views.createOrEditTransactionPage

                return Blazor.Component<Components.Pages.Home>
                       |> Blazor.SetParameters [
                           "model" => viewModel
                       ]
                       |> ExtendedActionResult.InvalidModelState
            | Ok partialTransaction ->
                let session = sessionUtil.LoadSessionFromPrincipal(this.User)

                match transactionId with
                | Some transactionId ->
                    let! _ = transactionDatabase.UpdateAsync session.UserId transactionId partialTransaction
                    ()
                | None ->
                    let! _ = transactionDatabase.CreateAsync session.UserId partialTransaction
                    ()

                return RedirectToAction (nameof this.GetBalanceView, "Balance")
        }

    [<HttpPost("new")>]
    member this.CreateTransactionAsync(
        [<FromBody; Required>] request: EditTransactionRequest
    ): Task<ExtendedActionResult> =
        if not this.ModelState.IsValid then
            Blazor.Component<Components.Pages.Home>
            |> Blazor.SetParameters [
                "model" => NewTransaction EditTransactionViewModel.Default
            ]
            |> InvalidModelState
            |> Task.FromResult
        else
            this.CreateOrEditTransaction(None, request)

    [<HttpPost("{transactionId}")>]
    member this.EditTransactionAsync(
        [<FromRoute; Required>] transactionId: int64,
        [<FromBody; Required>] request: EditTransactionRequest
    ): Task<ExtendedActionResult> =
        if not this.ModelState.IsValid then
            Blazor.Component<Components.Pages.Home>
            |> Blazor.SetParameters [
                "model" => NewTransaction EditTransactionViewModel.Default
            ]
            |> InvalidModelState
            |> Task.FromResult
        else
            this.CreateOrEditTransaction(Some transactionId, request)

    [<HttpPost("{transactionId}/delete")>]
    member this.DeleteTransactionAsync(
        [<FromRoute; Required>] transactionId: int64
    ): Task<ExtendedActionResult> =
        let session = sessionUtil.LoadSessionFromPrincipal(this.User)
        task {
            do! transactionDatabase.DeleteAsync session.UserId transactionId
            return RedirectToAction (nameof this.GetBalanceView, "Balance")
        }

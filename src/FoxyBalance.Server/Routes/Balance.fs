namespace FoxyBalance.Server.Routes

open FoxyBalance.Database.Interfaces
open FoxyBalance.Sync
open Giraffe
open FoxyBalance.Server
open FoxyBalance.Database.Models
open FoxyBalance.Server.Models.RequestModels
open FoxyBalance.Server.Models.ViewModels
open Microsoft.Extensions.DependencyInjection

module Views = FoxyBalance.Server.Views.Balance

module Balance =
    let homePageHandler : HttpHandler =
        RouteUtils.withSession(fun session next ctx -> task {
            let limit = 35
            let page =
                let parsedValue =
                    ctx.TryGetQueryStringValue "page"
                    |> Option.map int
                    |> Option.defaultValue 1
                if parsedValue < 0 then 1 else parsedValue
            let status =
                match ctx.TryGetQueryStringValue "status" |> Option.map String.toLower with
                | Some "pending" ->
                    PendingTransactions
                | Some "cleared" ->
                    ClearedTransactions
                | _ ->
                    AllTransactions
            let database = ctx.GetService<ITransactionDatabase>()
            let! transactions = database.ListAsync(session.UserId, { Limit = limit
                                                                     Offset = limit * (page - 1)
                                                                     Order = Descending
                                                                     Status = status })

            let! count = database.CountAsync(session.UserId, status)
            let! sum = database.SumAsync session.UserId 
            let model : HomePageViewModel =
                { Transactions = transactions
                  Sum = sum
                  Page = page
                  Status = status
                  TotalPages = if count % limit > 0 then (count / limit) + 1 else count / limit
                  TotalTransactions = count }
            
            return! htmlView (Views.homePage model) next ctx
        })
        
    let newTransactionHandler : HttpHandler =
        EditTransactionViewModel.Default
        |> NewTransaction
        |> Views.createOrEditTransactionPage
        |> htmlView
    
    let editTransactionHandler (transactionId : int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<ITransactionDatabase>()
            
            match! database.GetAsync(session.UserId, transactionId) with
            | Some transaction ->
                let view = 
                    (transactionId, EditTransactionViewModel.FromExistingTransaction transaction)
                    |> ExistingTransaction
                    |> Views.createOrEditTransactionPage
                    |> htmlView
                return! view next ctx
            | None ->
                return! (setStatusCode 404 >=> text "Not Found") next ctx 
        })
        
    let private createOrEditTransaction (transactionId : int64 option) =
        RouteUtils.withSession (fun session next ctx -> task {
            let! model = ctx.BindFormAsync<EditTransactionRequest>()
            
            match EditTransactionRequest.Validate model with
            | Error msg ->
                let view =
                    let viewModel = EditTransactionViewModel.FromBadRequest model msg
                    
                    transactionId
                    |> Option.map (fun i -> ExistingTransaction (i, viewModel))
                    |> Option.defaultWith (fun _ -> NewTransaction viewModel)
                    |> Views.createOrEditTransactionPage
                    |> htmlView
                    
                return! (view >=> setStatusCode 422) next ctx
            | Ok partialTransaction ->
                let database = ctx.GetService<ITransactionDatabase>()
                
                do! match transactionId with
                    | Some transactionId ->
                        database.UpdateAsync(session.UserId, transactionId, partialTransaction)
                        |> Task.Ignore 
                    | None ->
                        database.CreateAsync(session.UserId, partialTransaction)
                        |> Task.Ignore 
                
                return! redirectTo false "/balance" next ctx
        })
        
    let private deleteTransaction transactionId =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<ITransactionDatabase>()
            
            do! database.DeleteAsync(session.UserId, transactionId)
            
            return! redirectTo false "/balance" next ctx 
        })
        
    let newTransactionPostHandler : HttpHandler =
        createOrEditTransaction None 

    let existingTransactionPostHandler (transactionId : int64) : HttpHandler =
        Some transactionId
        |> createOrEditTransaction

    let deleteTransactionPostHandler (transactionId : int64) : HttpHandler =
        deleteTransaction transactionId

    let uploadTransactionsView : HttpHandler =
        UploadTransactionsViewModel.Default
        |> Views.Balance.uploadTransactionsPage
        |> htmlView

    let uploadTransactionsHandler : HttpHandler =
        failwith "not implemented"

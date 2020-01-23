namespace FoxyBalance.Server.Routes

open FSharp.Control.Tasks.V2.ContextInsensitive
open FoxyBalance.Database.Interfaces
open Giraffe
open FoxyBalance.Server
open FoxyBalance.Database.Models
open FoxyBalance.Server.Models.RequestModels
open FoxyBalance.Server.Models.ViewModels
module Views = FoxyBalance.Server.Views.Home

module Home =
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
            let! transactions =
                { Limit = limit
                  Offset = limit * (page - 1)
                  Order = Descending
                  Status = status }
                |> database.ListAsync session.UserId
            let! count = database.CountAsync session.UserId status
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
        Views.newTransactionPage NewTransactionViewModel.Default
        |> htmlView
        
    let newTransactionPostHandler : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let! model = ctx.BindFormAsync<CreateTransactionRequest>()
                
            match CreateTransactionRequest.Validate model with
            | Error msg ->
                let result =
                    NewTransactionViewModel.FromBadRequest model msg
                    |> Views.newTransactionPage
                    |> htmlView
                    >=> setStatusCode 422 
                return! result next ctx
            | Ok partialTransaction ->
                let database = ctx.GetService<ITransactionDatabase>()
                let! _ = database.CreateAsync session.UserId partialTransaction
                return! redirectTo false "/home" next ctx
        })

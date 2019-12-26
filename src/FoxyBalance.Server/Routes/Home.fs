namespace FoxyBalance.Server.Routes

open FSharp.Control.Tasks.V2.ContextInsensitive
open FoxyBalance.Database.Interfaces
open Giraffe
open FoxyBalance.Server
open FoxyBalance.Database.Models
open FoxyBalance.Server.Models.ViewModels
open Microsoft.AspNetCore.Http
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
            let database = ctx.GetService<ITransactionDatabase>()
            let! transactions =
                { Limit = limit
                  Offset = limit * (page - 1)
                  Order = Descending }
                |> database.ListAsync session.UserId
            let! count = database.CountAsync session.UserId
            let model : HomePageViewModel =
                { Transactions = transactions
                  Page = page
                  TotalPages = if count % limit > 0 then (count / limit) + 1 else count / limit
                  TotalTransactions = count }
            
            return! htmlView (Views.homePage model) next ctx
        })

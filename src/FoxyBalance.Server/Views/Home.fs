namespace FoxyBalance.Server.Views

open FoxyBalance.Server.Models.ViewModels
open Giraffe.GiraffeViewEngine

module Home =
    let homePage (model : HomePageViewModel) : XmlNode =
        let title = sprintf "Transactions - Page %i" model.Page
        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            p [] [
                sprintf "You're on the home page. You have %i transactions with %i total pages." model.TotalTransactions model.TotalPages
                |> str
            ]
            
            p [] [
                sprintf "You have a total of $%.2M in transactions, with $%.2M cleared and $%.2M pending." model.Sum.Sum model.Sum.ClearedSum model.Sum.PendingSum
                |> str
            ]
        ]

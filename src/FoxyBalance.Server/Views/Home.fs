﻿namespace FoxyBalance.Server.Views

open FoxyBalance.Server.Models.ViewModels
open Giraffe.GiraffeViewEngine

module Home =
    let homePage (model : HomePageViewModel) : XmlNode =
        let title = sprintf "Transactions - Page %i" model.Page
        Shared.pageContainer title Shared.WrappedInSection [
            sprintf "You're on the home page. You have %i transactions with %i total pages." model.TotalTransactions model.TotalPages
            |> str 
        ]

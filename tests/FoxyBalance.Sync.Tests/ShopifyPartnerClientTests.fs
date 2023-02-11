module FoxyBalance.Sync.Tests.ShopifyPartnerClientTests

open System
open FoxyBalance.Sync
open FoxyBalance.Sync.Models
open Xunit

let makeClient () = 
    let options = Configuration.configureShopifyPartnerOptions()
    ShopifyPartnerClient(options)

[<Fact(Skip = "Should only be run manually.")>]
let ``Should list transactions`` () =
    let client = makeClient()
    
    let rec complete output request = task {
        let! result = request
        // Combine this result's transactions with all other result's sales
        let output = Seq.append result.Transactions output
        
        match result.NextPageCursor with
        | Some cursor ->
            return!
                Some cursor
                |> client.ListTransactionsAsync
                |> complete output 
        | None ->
            return output
    }
    
    task {
        let! sales =
            client.ListTransactionsAsync None
            |> complete []
        let sum =
            sales
            |> Seq.sumBy (fun sale -> sale.NetAmount)
        let adjustments =
            sales
            |> Seq.choose (fun sale ->
                match sale.Type with
                | AppSaleAdjustment -> Some sale.NetAmount
                | _ -> None)
            |> Seq.sum
        let subscriptions =
            sales
            |> Seq.choose (fun sale ->
                match sale.Type with
                | AppSubscriptionSale -> Some sale.NetAmount
                | _ -> None)
            |> Seq.sum
        let credits =
            sales
            |> Seq.choose (fun sale ->
                match sale.Type with
                | AppSaleCredit -> Some sale.NetAmount
                | _ -> None)
            |> Seq.sum
        
        Assert.Equal(sum, subscriptions + adjustments + credits)
    }

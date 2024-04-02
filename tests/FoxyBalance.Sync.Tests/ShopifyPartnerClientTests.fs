module FoxyBalance.Sync.Tests.ShopifyPartnerClientTests

open FoxyBalance.Sync
open FoxyBalance.Sync.Models
open ShopifySharp
open System.Net.Http
open NSubstitute
open Xunit

let makeClient () =
    let options = Configuration.configureShopifyPartnerOptions()
    let mockedExecutionPolicy = Substitute.For<IRequestExecutionPolicy>()
    let mockedHttpClientFactory = Substitute.For<IHttpClientFactory>()
    ShopifyPartnerClient(options, mockedExecutionPolicy, mockedHttpClientFactory)

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
        let negative =
            sales
            |> Seq.filter (fun s -> s.GrossAmount < 0 && s.Type = AppSubscriptionSale)
        
        Assert.Equal(sum, subscriptions + adjustments + credits)
    }
    
[<Fact(Skip = "Should only be run manually.")>]
let ``Should get a transaction`` () =
    let client = makeClient()
    let transactionId = "gid://partners/AppSubscriptionSale/194047357"
    
    task {
        let! transaction = client.GetTransaction(transactionId)
        
        Assert.True(Option.isSome transaction)
        
        let transaction = Option.get transaction
        
        Assert.True(transaction.Id = transactionId)
        Assert.True(transaction.GrossAmount > 0)
        Assert.True(transaction.ProcessingFee > 0)
        Assert.True(transaction.NetAmount > 0)
        Assert.True(transaction.ShopifyFee = 0)
        Assert.True(transaction.Type = AppSubscriptionSale)
        Assert.True(transaction.Description = "Subscription to Stages")
        Assert.True(Option.isSome transaction.App)
    }
    
[<Fact(Skip = "Should only be run manually.")>]
let ``Should get a transaction that does not exist`` () =
    let client = makeClient()
    let transactionId = "gid://partners/AppSubscriptionSale/1234"
    
    task {
        let! transaction = client.GetTransaction(transactionId)
        
        Assert.True(Option.isNone transaction)
    }

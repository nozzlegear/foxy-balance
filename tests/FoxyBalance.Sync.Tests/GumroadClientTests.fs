module FoxyBalance.Sync.Tests.GumroadClientTests

open System
open FoxyBalance.Sync
open FoxyBalance.Sync.Models
open Xunit

let makeClient () = 
    let options = Configuration.configureGumroadOptions()
    let httpClientFactory = Configuration.configureHttpClientFactory()
    GumroadClient(options, httpClientFactory)

[<Fact(Skip = "Should only be run manually.")>]
let ``Should list sales`` () =
    let client = makeClient()
    
    task {
        let! result = client.ListAsync(1)
       
        Assert.NotNull(result) 
        Assert.NotEmpty(result.Sales)
    }
    
[<Fact(Skip = "Should only be run manually.")>]
let ``Should list multiple pages of sales`` () =
    let client = makeClient()
    let after = DateTimeOffset.Parse("2022-01-01")
    
    task {
        let! result = client.ListAsync(1, after)

        Assert.True(Option.isSome result.NextPageUrl)

        let! result = client.ListAsync(Option.get result.NextPageUrl)
       
        Assert.NotEmpty(result.Sales)
        Assert.True(Option.isSome result.PreviousPageUrl)
    }
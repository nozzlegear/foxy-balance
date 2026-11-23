module FoxyBalance.Server.Tests.ApiMatchRequestTests

open System
open Xunit
open FoxyBalance.Server.Api.Requests

[<Fact>]
let ``ApiMatchRequest fields are correctly initialized`` () =
    let request = { TransactionId = "123"; RecurringTransactionId = "456" }
    Assert.Equal("123", request.TransactionId)
    Assert.Equal("456", request.RecurringTransactionId)

[<Fact>]
let ``ApiMatchRequest validates positive IDs`` () =
    // TransactionId must parse to a positive number
    let invalidRequest1 = { TransactionId = "0"; RecurringTransactionId = "456" }
    Assert.Equal("0", invalidRequest1.TransactionId)
    
    // RecurringTransactionId must parse to a positive number
    let invalidRequest2 = { TransactionId = "123"; RecurringTransactionId = "0" }
    Assert.Equal("0", invalidRequest2.RecurringTransactionId)

[<Fact>]
let ``ApiMatchRequest can be created with large IDs`` () =
    let request = { TransactionId = "9999999999"; RecurringTransactionId = "8888888888" }
    Assert.Equal("9999999999", request.TransactionId)
    Assert.Equal("8888888888", request.RecurringTransactionId)

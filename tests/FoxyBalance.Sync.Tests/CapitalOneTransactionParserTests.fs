namespace FoxyBalance.Sync.Tests

open System
open FoxyBalance.Sync
open Xunit
open Faqt
open Faqt.Operators
open FoxyBalance.Sync.Models

type CapitalOneTransactionParserTests() =
    let [<Literal>] HeaderRow = """Account Number,Transaction Description,Transaction Date,Transaction Type,Transaction Amount,Balance"""

    let sut = CapitalOneTransactionParser()

    [<Fact>]
    member _.``Should parse Capital One transactions``() =
        // Setup
        let data = $"""
{HeaderRow}
1122,Debit Card Purchase - MERCHANT NAME 1165 TOWN MN,11/27/25,Debit,21,405.12
1122,Deposit from FOO B . BAZ PAY 123456,11/26/25,Credit,504.48,601.12
"""

        // Act
        let result = sut.FromCsv data

        // Assert
        %result.Should().HaveLength 2
        %result.Should().Be [
            { Id = "6DYSWL"
              DateCreated = DateTimeOffset(2025, 11, 27, 0, 0, 0, TimeSpan.Zero)
              AccountNumber = "1122"
              Description = "Debit Card Purchase - MERCHANT NAME 1165 TOWN MN"
              Type = CapitalOneTransactionType.Debit
              Amount = 21.00M
              Balance =  405.12M }
            { Id = "TRWHC9"
              DateCreated = DateTimeOffset(2025, 11, 26, 0, 0, 0, TimeSpan.Zero)
              AccountNumber = "1122"
              Description = "Deposit from FOO B . BAZ PAY 123456"
              Type = CapitalOneTransactionType.Credit
              Amount = 504.48M
              Balance =  601.12M }
        ]

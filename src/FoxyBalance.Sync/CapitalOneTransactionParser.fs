namespace FoxyBalance.Sync

open System
open System.IO
open System.IO.Hashing
open System.Runtime.CompilerServices
open System.Text
open BigNumber
open FSharp.Data
open FoxyBalance.Sync.Models

type CapitalOneTransactions = CsvProvider<"../../assets/capital-one-example-transactions.csv", Schema = "Transaction Date=string">

type CapitalOneTransactionParser() =
    let mkTransactionId (row: CapitalOneTransactions.Row): string =
        let identifier =
            row.``Transaction Date``
            + "_" + string row.``Account Number``
            + "_" + row.``Transaction Type``
            + "_" + row.``Transaction Description``
            + "_" + string row.``Transaction Amount``
            + "_" + string row.Balance

        Encoding.UTF8.GetBytes identifier
        |> Crc32.Hash
        |> Base36Number
        |> _.ToString()

    let parseTransactionDate (transactionDate: string): DateTimeOffset =
        let date = DateOnly.ParseExact(transactionDate, "MM/dd/yy")
        // Since Capital One does not record time information, assume the dates are recorded in UTC
        let offset = TimeSpan.Zero
        DateTimeOffset(date, TimeOnly(0), offset = offset)

    let parseTransactionType (transactionType: string): CapitalOneTransactionType =
        if transactionType.Equals("debit", StringComparison.OrdinalIgnoreCase) then
            CapitalOneTransactionType.Debit
        elif transactionType.Equals("credit", StringComparison.OrdinalIgnoreCase) then
            CapitalOneTransactionType.Credit
        else
            raise (SwitchExpressionException transactionType)

    let mapTransactions (csv : CapitalOneTransactions): CapitalOneTransaction list =
        csv.Rows
        |> Seq.map (fun row ->
            { Id = mkTransactionId row
              DateCreated = parseTransactionDate row.``Transaction Date``
              AccountNumber = string row.``Account Number``
              Description = row.``Transaction Description``
              Type = parseTransactionType row.``Transaction Type``
              Amount = row.``Transaction Amount``
              Balance =  row.Balance })
        |> List.ofSeq

    member _.FromCsv (fileText: string) =
        CapitalOneTransactions.Parse fileText
        |> mapTransactions

    member _.FromCsvStream (stream: Stream) =
        CapitalOneTransactions.Load stream
        |> mapTransactions

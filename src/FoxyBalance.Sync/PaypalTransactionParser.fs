namespace FoxyBalance.Sync

open System
open System.Globalization
open System.IO
open FSharp.Data
open FoxyBalance.Sync.Models

type PaypalTransactions = CsvProvider<"../../assets/paypal-transaction-activity.csv", Schema = "Date (string)">

type PaypalTransactionParser() =
    // Timezones seem to be either PST or PDT
    let tz = TimeZoneInfo.FindSystemTimeZoneById "America/Los_Angeles"

    let parsePaypalDate (row: PaypalTransactions.Row) =
        let date = $"{row.Date} {row.Time}"
        let format = "MM/dd/yyyy HH:mm:ss"
        let culture = CultureInfo.InvariantCulture
        let dt = DateTime.ParseExact(date, format, culture)
        DateTimeOffset(dt, tz.GetUtcOffset(dt))

    let parsePaypalAmount amt =
        int (amt * 100M)

    [<Literal>]
    let invoiceTransactionType = "T0007"

    let isInvoice (row: PaypalTransactions.Row): bool = 
        row.``Transaction Event Code`` = invoiceTransactionType
        && row.Gross > 0M
        && row.``Invoice Number``.HasValue
    
    let parse (csv : PaypalTransactions): PaypalInvoice seq =
        csv.Rows
        |> Seq.filter isInvoice
        |> Seq.map (fun row -> 
            let customer = row.Name
            let invoice = row.``Invoice Number``.Value
            {
                Id = row.``Reference Txn ID``
                DateCreated = parsePaypalDate row
                Customer = customer
                Gross = parsePaypalAmount row.Gross
                Discount = parsePaypalAmount (decimal row.Discount)
                // The fee is a negative number here. Convert it to positive.
                Fee = (parsePaypalAmount row.Fee) * -1
                Net = parsePaypalAmount row.Net
                InvoiceNumber = string invoice
                Description = $"Invoice {invoice} to {customer}"
            })

    member _.FromCsv (fileText: string) =
        PaypalTransactions.Parse fileText
        |> parse
        
    member _.FromCsv (file : Stream) =
        PaypalTransactions.Load file
        |> parse

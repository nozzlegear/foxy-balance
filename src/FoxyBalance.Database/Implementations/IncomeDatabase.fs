namespace FoxyBalance.Database

open System.Data
open FoxyBalance.Database.Models
open FoxyBalance.Database.Interfaces
open DustyTables

type IncomeDatabase(options : IDatabaseOptions) =
    let connection =
        Sql.connect options.ConnectionString
        |> Sql.timeout 90
        
    let incomeSourceToSql = function
        | Paypal  x -> {| SourceType = "paypal"; TransactionId = Some x.TransactionId; Description = x.Description |}
        | Stripe  x -> {| SourceType = "stripe"; TransactionId = Some x.TransactionId; Description = x.Description |}
        | Gumroad x -> {| SourceType = "gumroad"; TransactionId = Some x.TransactionId; Description = x.Description |}
        | Shopify x -> {| SourceType = "shopify"; TransactionId = Some x.TransactionId; Description = x.Description |}
        | ManualTransaction x -> {| SourceType = "manual-transaction"; TransactionId = None; Description = x.Description |}
        
    let incomeSourceFromSql (read : RowReader) =
        let readSourceDescription (sourceType : IncomeSourceDescription -> IncomeSource) =
            sourceType {
                TransactionId = read.string "SourceTransactionId"
                Description = read.string "SourceTransactionDescription"
            }
        
        match read.string "SourceType" with
        | "paypal"  -> readSourceDescription Paypal
        | "stripe"  -> readSourceDescription Stripe
        | "gumroad" -> readSourceDescription Gumroad
        | "shopify" -> readSourceDescription Shopify
        | "manual-transaction" -> ManualTransaction { Description = read.string "ManualTransactionDescription" }
        | x -> invalidArg "SourceType" $"Unhandled income SourceType value \"{x}\", cannot map to IncomeSource"
        
    let taxYearFromSql (read : RowReader) =
        {
            Id = read.int "TaxYearId"
            TaxYear = read.int "TaxYear"
            TaxRate = read.int "TaxRate"
        }

    let partialRecords (records : PartialIncomeRecord seq) =
        let dataTable = new DataTable()
        dataTable.Columns.Add "SaleDate" |> ignore
        dataTable.Columns.Add "SourceType" |> ignore
        dataTable.Columns.Add "SourceTransactionId" |> ignore
        dataTable.Columns.Add "SourceTransactionDescription" |> ignore
        dataTable.Columns.Add "SaleAmount" |> ignore
        dataTable.Columns.Add "PlatformFee" |> ignore
        dataTable.Columns.Add "ProcessingFee" |> ignore
        dataTable.Columns.Add "NetShare" |> ignore
        
        for record in records do
            let source = incomeSourceToSql record.Source
            
            dataTable.Rows.Add [|
                box <| record.SaleDate
                box <| source.SourceType
                box <| Option.defaultValue null source.TransactionId
                box <| source.Description
                box <| record.SaleAmount
                box <| record.PlatformFee
                box <| record.ProcessingFee
                box <| record.NetShare
            |] |> ignore
            
        Sql.table ("tp_PartialIncomeRecord", dataTable)

    interface IIncomeDatabase with
        member _.ImportAsync userId records =
            let data = [
                "@userId", Sql.int userId
                "@partialIncomeRecords", partialRecords records
            ]
            
            connection
            |> Sql.storedProcedure "sp_BatchImportIncomeRecords"
            |> Sql.parameters data
            |> Sql.executeRowAsync (fun read ->
                {
                    TotalNewRecordsImported = read.int "TotalNewRecordsImported"
                    TotalSalesImported = read.int "TotalSalesImported"
                    TotalFeesImported = read.int "TotalFeesImported"
                    TotalNetShareImported = read.int "TotalNetShareImported"
                    TotalEstimatedTaxesImported = read.decimal "TotalEstimatedTaxesImported"
                })
            
        member _.ListAsync userId taxYear =
            connection
            |> Sql.query """
                SELECT TOP 100 *
                FROM [FoxyBalance_IncomeRecordsView]
                WHERE [UserId] = @userId
                AND [TaxYear] = @taxYear
                ORDER BY [SaleDate] DESC
            """
            |> Sql.parameters [
                "userId", Sql.int userId
                "taxYear", Sql.int taxYear
            ]
            |> Sql.executeAsync (fun read ->
                {
                    Id = read.int64 "Id"
                    Source = incomeSourceFromSql read
                    SaleDate = read.dateTimeOffset "SaleDate"
                    SaleAmount = read.int "SaleAmount"
                    PlatformFee = read.int "PlatformFee"
                    ProcessingFee = read.int "ProcessingFee"
                    NetShare = read.int "NetShare"
                    EstimatedTax = read.int "EstimatedTax"
                    Ignored = read.bool "Ignored"
                })
            |> Sql.map Seq.ofList
            
        member _.SummarizeAsync userId taxYear =
            connection
            |> Sql.query """
                SELECT TOP 1 *
                FROM [FoxyBalance_TaxYearSummaryView]
                WHERE [UserId] = @userId
                AND [TaxYear] = @taxYear
            """
            |> Sql.parameters [
                "userId", Sql.int userId
                "taxYear", Sql.int taxYear
            ]
            |> Sql.executeAsync (fun read ->
                {
                    TaxYear = taxYearFromSql read
                    TotalRecords = read.int "TotalRecords"
                    TotalSales = read.int "TotalSales"
                    TotalFees = read.int "TotalFees"
                    TotalNetShare = read.int "TotalNetShare"
                    TotalEstimatedTax = int (read.decimal "TotalEstimatedTax")
                })
            |> Sql.map Seq.tryHead
            
        member _.IgnoreAsync userId incomeId =
            failwith "not implemented"
            
        member _.UnignoreAsync userId incomeId =
            failwith "not implemented"
            
        member _.DeleteAsync userId incomeId =
            failwith "not implemented"

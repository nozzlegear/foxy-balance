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
        | Paypal  x -> {| SourceType = "paypal"; TransactionId = Some x.TransactionId; Description = x.Description; Customer = Some x.CustomerDescription |}
        | Stripe  x -> {| SourceType = "stripe"; TransactionId = Some x.TransactionId; Description = x.Description; Customer = Some x.CustomerDescription |}
        | Gumroad x -> {| SourceType = "gumroad"; TransactionId = Some x.TransactionId; Description = x.Description; Customer = Some x.CustomerDescription |}
        | Shopify x -> {| SourceType = "shopify"; TransactionId = Some x.TransactionId; Description = x.Description; Customer = Some x.CustomerDescription |}
        | ManualTransaction x -> {| SourceType = "manual-transaction"; TransactionId = None; Description = x.Description; Customer = x.CustomerDescription |}
        
    let incomeSourceFromSql (read : RowReader) =
        let readSourceDescription (sourceType : IncomeSourceDescription -> IncomeSource) =
            sourceType {
                TransactionId = read.string "SourceTransactionId"
                Description = read.string "SourceTransactionDescription"
                CustomerDescription = read.string "SourceTransactionCustomerDescription"
            }
        let readManualSourceDescription (sourceType : ManualIncomeSourceDescription -> IncomeSource) =
            sourceType {
                Description = read.string "SourceTransactionDescription"
                CustomerDescription = read.stringOrNone "SourceTransactionCustomerDescription"
            }
        
        match read.string "SourceType" with
        | "paypal"  -> readSourceDescription Paypal
        | "stripe"  -> readSourceDescription Stripe
        | "gumroad" -> readSourceDescription Gumroad
        | "shopify" -> readSourceDescription Shopify
        | "manual-transaction" -> readManualSourceDescription ManualTransaction
        | x -> invalidArg "SourceType" $"Unhandled income SourceType value \"{x}\", cannot map to IncomeSource"
        
    let taxYearFromSql (read : RowReader) =
        {
            TaxYear = read.int "TaxYear"
            TaxRate = read.int "TaxRate"
        }
        
    let incomeRecordFromSql (read: RowReader) =
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
        }
        
    let incomeSummaryFromSql (read: RowReader) =
        {
            TaxYear = taxYearFromSql read
            TotalRecords = read.int "TotalRecords"
            TotalSales = read.int "TotalSales"
            TotalFees = read.int "TotalFees"
            TotalNetShare = read.int "TotalNetShare"
            TotalEstimatedTax = int (read.decimal "TotalEstimatedTax")
        }

    let partialRecords (records : PartialIncomeRecord seq) =
        let dataTable = new DataTable()
        dataTable.Columns.Add "SaleDate" |> ignore
        dataTable.Columns.Add "SourceType" |> ignore
        dataTable.Columns.Add "SourceTransactionId" |> ignore
        dataTable.Columns.Add "SourceTransactionDescription" |> ignore
        dataTable.Columns.Add "SourceTransactionCustomerDescription" |> ignore
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
                box <| Option.defaultValue null source.Customer
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
            
        member _.ListAsync userId taxYear options =
            connection
            |> Sql.query """
                SELECT *
                FROM [FoxyBalance_IncomeRecordsView]
                WHERE [UserId] = @userId
                  AND [TaxYear] = @taxYear
                ORDER BY
                    CASE WHEN @direction = 'ASC' THEN [SaleDate] END ASC,
                    CASE WHEN @direction = 'DESC' THEN [SaleDate] END DESC
                OFFSET @offset ROWS
                FETCH NEXT @recordLimit ROWS ONLY;
            """
            |> Sql.parameters [
                "userId", Sql.int userId
                "taxYear", Sql.int taxYear
                "recordLimit", Sql.int options.Limit
                "offset", Sql.int options.Offset
                "direction", Sql.string (if options.Order = Order.Ascending then "ASC" else "DESC")
            ]
            |> Sql.executeAsync incomeRecordFromSql
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
            |> Sql.executeAsync incomeSummaryFromSql
            |> Sql.map Seq.tryHead
            
        member _.SetIgnoreAsync userId incomeId shouldIgnore =
            connection
            |> Sql.query """
                UPDATE [FoxyBalance_IncomeRecords]
                SET [Ignored] = @ignored
                WHERE [UserId] = @userId
                AND [Id] = @recordId
            """
            |> Sql.parameters [
                "ignored", Sql.bool shouldIgnore
                "userId", Sql.int userId
                "recordId", Sql.int64 incomeId
            ]
            |> Sql.executeNonQueryAsync
            |> Sql.ignore
            
        member _.DeleteAsync userId incomeId =
            connection
            |> Sql.query """
                DELETE FROM [FoxyBalance_IncomeRecords]
                WHERE [UserId] = @userId
                AND [Id] = @recordId
            """
            |> Sql.parameters [
                "userId", Sql.int userId
                "recordId", Sql.int64 incomeId
            ]
            |> Sql.executeNonQueryAsync
            |> Sql.ignore

        member _.ListTaxYearsAsync userId =
            connection
            |> Sql.query """
                SELECT
                    TaxYear,
                    TaxRate
                FROM [FoxyBalance_TaxYearSummaryView]
                WHERE [UserId] = @userId
            """
            |> Sql.parameters [
                "userId", Sql.int userId
            ]
            |> Sql.executeAsync taxYearFromSql
            |> Sql.map Seq.ofList
            
        member _.GetTaxYearAsync userId taxYear =
            connection
            |> Sql.query """
                SELECT TOP 1
                    TaxYear,
                    TaxRate
                FROM [FoxyBalance_TaxYearSummaryView]
                WHERE [UserId] = @userId
                AND [TaxYear] = @taxYear
            """
            |> Sql.parameters [
                "userId", Sql.int userId
                "taxYear", Sql.int taxYear
            ]
            |> Sql.executeAsync taxYearFromSql
            |> Sql.map Seq.tryHead
            
        member _.SetTaxYearRateAsync userId taxYear rate =
            connection
            |> Sql.query """
                UPDATE [FoxyBalance_TaxYears]
                SET [TaxRate] = @taxRate
                WHERE [UserId] = @userId
                AND [TaxYear] = @taxYear
            """
            |> Sql.parameters [
                "taxRate", Sql.int rate
                "userId", Sql.int userId
                "taxYear", Sql.int taxYear
            ]
            |> Sql.executeNonQueryAsync
            |> Sql.ignore

        member _.GetAsync userId incomeId =
            connection
            |> Sql.query """
                SELECT *
                FROM [FoxyBalance_IncomeRecordsView]
                WHERE [UserId] = @userId
                AND [Id] = @incomeId
            """
            |> Sql.parameters [
                "userId", Sql.int userId
                "incomeId", Sql.int64 incomeId
            ]
            |> Sql.executeAsync incomeRecordFromSql
            |> Sql.map Seq.tryHead

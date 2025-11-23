namespace FoxyBalance.Database

open System.Text.Json
open FoxyBalance.Database.Models
open FoxyBalance.Database.Interfaces
open Npgsql.FSharp

type IncomeDatabase(options : IDatabaseOptions) =
    let connection =
        Sql.connect options.ConnectionString

    let incomeSourceToSql = function
        | Paypal  x -> {| SourceType = "paypal"; TransactionId = Some x.TransactionId; Description = x.Description; Customer = Some x.CustomerDescription |}
        | Stripe  x -> {| SourceType = "stripe"; TransactionId = Some x.TransactionId; Description = x.Description; Customer = Some x.CustomerDescription |}
        | Gumroad x -> {| SourceType = "gumroad"; TransactionId = Some x.TransactionId; Description = x.Description; Customer = Some x.CustomerDescription |}
        | Shopify x -> {| SourceType = "shopify"; TransactionId = Some x.TransactionId; Description = x.Description; Customer = Some x.CustomerDescription |}
        | ManualTransaction x -> {| SourceType = "manual-transaction"; TransactionId = None; Description = x.Description; Customer = x.CustomerDescription |}
        
    let incomeSourceFromSql (read : RowReader) =
        let readSourceDescription (sourceType : IncomeSourceDescription -> IncomeSource) =
            sourceType {
                TransactionId = read.string "sourcetransactionid"
                Description = read.string "sourcetransactiondescription"
                CustomerDescription = read.string "sourcetransactioncustomerdescription"
            }
        let readManualSourceDescription (sourceType : ManualIncomeSourceDescription -> IncomeSource) =
            sourceType {
                Description = read.string "sourcetransactiondescription"
                CustomerDescription = read.stringOrNone "sourcetransactioncustomerdescription"
            }

        match read.string "sourcetype" with
        | "paypal"  -> readSourceDescription Paypal
        | "stripe"  -> readSourceDescription Stripe
        | "gumroad" -> readSourceDescription Gumroad
        | "shopify" -> readSourceDescription Shopify
        | "manual-transaction" -> readManualSourceDescription ManualTransaction
        | x -> invalidArg "SourceType" $"Unhandled income SourceType value \"{x}\", cannot map to IncomeSource"
        
    let taxYearFromSql (read : RowReader) =
        {
            TaxYear = read.int "taxyear"
            TaxRate = read.int "taxrate"
        }

    let incomeRecordFromSql (read: RowReader) =
        {
            Id = read.int64 "id"
            Source = incomeSourceFromSql read
            SaleDate = read.datetimeOffset "saledate"
            SaleAmount = read.int "saleamount"
            PlatformFee = read.int "platformfee"
            ProcessingFee = read.int "processingfee"
            NetShare = read.int "netshare"
            EstimatedTax = read.int "estimatedtax"
            Ignored = read.bool "ignored"
        }

    let incomeSummaryFromSql (read: RowReader) =
        {
            TaxYear = taxYearFromSql read
            TotalRecords = read.int "totalrecords"
            TotalSales = read.int "totalsales"
            TotalFees = read.int "totalfees"
            TotalNetShare = read.int "totalnetshare"
            TotalEstimatedTax = int (read.decimal "totalestimatedtax")
        }

    let partialRecordsJson (records : PartialIncomeRecord seq) =
        records
        |> Seq.map (fun record ->
            let source = incomeSourceToSql record.Source
            {|
                SaleDate = record.SaleDate
                SourceType = source.SourceType
                SourceTransactionId = source.TransactionId
                SourceTransactionDescription = source.Description
                SourceTransactionCustomerDescription = source.Customer
                SaleAmount = record.SaleAmount
                PlatformFee = record.PlatformFee
                ProcessingFee = record.ProcessingFee
                NetShare = record.NetShare
            |})
        |> JsonSerializer.Serialize

    interface IIncomeDatabase with
        member _.ImportAsync userId records =
            let recordsJson = partialRecordsJson records

            connection
            |> Sql.query "SELECT * FROM batch_import_income_records(@userId, @records::jsonb)"
            |> Sql.parameters [
                "userId", Sql.int userId
                "records", Sql.string recordsJson
            ]
            |> Sql.executeRowAsync (fun read ->
                {
                    TotalNewRecordsImported = read.int "total_new_records_imported"
                    TotalSalesImported = read.int "total_sales_imported"
                    TotalFeesImported = read.int "total_fees_imported"
                    TotalNetShareImported = read.int "total_net_share_imported"
                    TotalEstimatedTaxesImported = read.decimal "total_estimated_taxes_imported"
                })
            
        member _.ListAsync userId taxYear options =
            connection
            |> Sql.query """
                SELECT *
                FROM foxybalance_incomerecordsview
                WHERE userid = @userId
                  AND taxyear = @taxYear
                ORDER BY
                    CASE WHEN @direction = 'ASC' THEN saledate END ASC,
                    CASE WHEN @direction = 'DESC' THEN saledate END DESC
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
                SELECT *
                FROM foxybalance_taxyearsummaryview
                WHERE userid = @userId
                AND taxyear = @taxYear
                LIMIT 1
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
                UPDATE foxybalance_incomerecords
                SET ignored = @ignored
                WHERE userid = @userId
                AND id = @recordId
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
                DELETE FROM foxybalance_incomerecords
                WHERE userid = @userId
                AND id = @recordId
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
                    taxyear,
                    taxrate
                FROM foxybalance_taxyearsummaryview
                WHERE userid = @userId
            """
            |> Sql.parameters [
                "userId", Sql.int userId
            ]
            |> Sql.executeAsync taxYearFromSql
            |> Sql.map Seq.ofList

        member _.GetTaxYearAsync userId taxYear =
            connection
            |> Sql.query """
                SELECT
                    taxyear,
                    taxrate
                FROM foxybalance_taxyearsummaryview
                WHERE userid = @userId
                AND taxyear = @taxYear
                LIMIT 1
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
                UPDATE foxybalance_taxyears
                SET taxrate = @taxRate
                WHERE userid = @userId
                AND taxyear = @taxYear
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
                FROM foxybalance_incomerecordsview
                WHERE userid = @userId
                AND id = @incomeId
            """
            |> Sql.parameters [
                "userId", Sql.int userId
                "incomeId", Sql.int64 incomeId
            ]
            |> Sql.executeAsync incomeRecordFromSql
            |> Sql.map Seq.tryHead

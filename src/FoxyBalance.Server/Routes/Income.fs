namespace FoxyBalance.Server.Routes

open Giraffe
open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open FoxyBalance.Database.Interfaces
open FoxyBalance.Sync
open FoxyBalance.Server
open FoxyBalance.Server.Models
open FoxyBalance.Server.Models.RequestModels
open FoxyBalance.Server.Models.ViewModels
open FoxyBalance.Database.Models
module Views = FoxyBalance.Server.Views.Income

module Income =
    let homePageHandler : HttpHandler =
        RouteUtils.withSession(fun session next ctx -> task {
            let limit = 35
            let page =
                let parsedValue =
                    ctx.TryGetQueryStringValue "page"
                    |> Option.map int
                    |> Option.defaultValue 1
                if parsedValue < 0 then 1 else parsedValue
            let taxYear =
                ctx.TryGetQueryStringValue "year"
                |> Option.map int
                |> Option.defaultValue System.DateTimeOffset.UtcNow.Year
            let database = ctx.GetService<IIncomeDatabase>()
            let! records = database.ListAsync session.UserId taxYear
            let! taxYears = database.ListTaxYearsAsync session.UserId
            let! summary = database.SummarizeAsync session.UserId taxYear
            let summary = Option.defaultValue IncomeSummary.Default summary
                
            let count = summary.TotalRecords
            let model : IncomeViewModel =
                { IncomeRecords = records
                  Summary = summary
                  Page = page
                  TaxYear = taxYear
                  TaxYears = taxYears
                  TotalPages = if count % limit > 0 then (count / limit) + 1 else count / limit
                  TotalRecordsForYear = count }
            
            return! htmlView (Views.homePage model) next ctx
        })

    let syncHandler : HttpHandler = 
        SyncShopifySalesViewModel.Default
        |> Views.Income.syncShopifySalesPage
        |> htmlView

    let private importShopifyTransactions (ctx: HttpContext) (file: IFormFile) (session: Session) = task {
        let database = ctx.GetService<IIncomeDatabase>()
        let shopifyParser = ctx.GetService<ShopifyPayoutParser>()

        return!
            file.OpenReadStream()
            |> shopifyParser.FromCsv
            |> Seq.map (fun sale ->
                {
                    Source = Shopify 
                        {
                            TransactionId = sale.Id
                            Description = sale.Description
                            CustomerDescription = sale.CustomerDescription
                        }
                    SaleDate = sale.SaleDate
                    SaleAmount = sale.SaleAmount
                    PlatformFee = sale.ShopifyFee
                    ProcessingFee = sale.ProcessingFee
                    NetShare = sale.PartnerShare
                })
            |> database.ImportAsync session.UserId
    }

    let private importPaypalTransactions (ctx: HttpContext) (file: IFormFile) (session: Session)  = task {
        let database = ctx.GetService<IIncomeDatabase>()
        let paypalParser = ctx.GetService<PaypalTransactionParser>()

        return!
            file.OpenReadStream()
            |> paypalParser.FromCsv
            |> Seq.map (fun sale ->
                {
                    Source = Paypal 
                        { 
                            TransactionId = sale.Id
                            Description = sale.Description
                            CustomerDescription = sale.CustomerDescription
                        }
                    SaleDate = sale.DateCreated;
                    SaleAmount = sale.Gross
                    PlatformFee = sale.Fee
                    ProcessingFee = 0
                    NetShare = sale.Net
                })
            |> database.ImportAsync session.UserId
    }

    let private importGumroadTransactions (ctx: HttpContext) (session: Session) = task {
        let database = ctx.GetService<IIncomeDatabase>()
        let gumroad = ctx.GetService<GumroadClient>()
        // TODO: determine which tax year to sync
        let! gumroadSales = gumroad.ListAllAsync(DateTimeOffset.Parse "2022-01-01")

        return!
            gumroadSales
            |> Seq.map (fun sale ->
                {
                    Source = Gumroad 
                        { 
                            TransactionId = sale.Id; 
                            Description = sale.Description 
                            CustomerDescription = sale.CustomerDescription
                        }
                    SaleDate = sale.CreatedAt
                    SaleAmount = sale.Price
                    PlatformFee = sale.GumroadFee
                    ProcessingFee = 0
                    NetShare = sale.Price - sale.GumroadFee 
                })
            |> database.ImportAsync session.UserId
    }
        
    let executeSyncHandler : HttpHandler =
        RouteUtils.withSession(fun session next ctx -> task {
            let shopifyFile = ctx.Request.Form.Files.GetFile "shopifyCsvFile"
            let paypalFile = ctx.Request.Form.Files.GetFile "paypalCsvFile"
            let syncGumroad = ctx.Request.Form.ContainsKey "syncGumroad"

            // Import gumroad transactions and each file where available
            let! tasks = Task.WhenAll [
                if not (isNull shopifyFile) then 
                    importShopifyTransactions ctx shopifyFile session

                if not (isNull paypalFile) then
                    importPaypalTransactions ctx paypalFile session

                if syncGumroad then
                    importGumroadTransactions ctx session
            ]
            let totalNewRecords = Seq.sumBy (fun t -> t.TotalNewRecordsImported) tasks

            return! redirectTo false $"/income?totalImported={totalNewRecords}" next ctx
        })

    let newRecordHandler : HttpHandler =
        NewIncomeRecordViewModel.Empty
        |> Views.createRecordPage
        |> htmlView

    let executeNewRecordHandler : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let! model = ctx.BindFormAsync<NewIncomeRecordRequest>()

            match NewIncomeRecordRequest.Validate model with
            | Result.Error err ->
                let view =
                    NewIncomeRecordViewModel.FromBadRequest model err
                    |> Views.createRecordPage
                    |> htmlView
                return! view next ctx
            | Result.Ok partialRecord ->
                let database = ctx.GetService<IIncomeDatabase>()
                let! _ = database.ImportAsync session.UserId [partialRecord]

                return! redirectTo false $"/income?totalImported=1" next ctx
        })

    let recordDetailsHandler (recordId: int64) : HttpHandler =
        RouteUtils.withSession(fun session next ctx -> task {
            let database = ctx.GetService<IIncomeDatabase>()

            match! database.GetAsync session.UserId recordId with
            | Some record ->
                let view = 
                    { IncomeRecord = record }
                    |> Views.recordDetailsPage
                    |> htmlView
                return! view next ctx
            | None ->
                return! (setStatusCode 404 >=> text "Not Found") next ctx 
        })

    let executeToggleIgnoreHandler (recordId: int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<IIncomeDatabase>()
            
            match! database.GetAsync session.UserId recordId with
            | Some record ->
                match record.Source with
                | ManualTransaction _ ->
                    return! (setStatusCode 422 >=> text $"Manual transaction income records cannot be ignored, they can only be deleted.") next ctx
                | _ ->
                    do! database.SetIgnoreAsync session.UserId recordId (not record.Ignored)
                    return! (redirectTo false $"/income/{recordId}") next ctx
            | None ->
                return! (setStatusCode 404 >=> text "Not Found") next ctx
        })
        
    let executeDeleteHandler (recordId: int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<IIncomeDatabase>()
            
            match! database.GetAsync session.UserId recordId with
            | Some record ->
                match record.Source with
                | ManualTransaction _ ->
                    do! database.DeleteAsync session.UserId recordId
                    return! (redirectTo false $"/income?year={record.SaleDate.Year}") next ctx
                | source ->
                    let formattedSource = Format.incomeSourceType source
                    return! (setStatusCode 422 >=> text $"{formattedSource} income records cannot be deleted, they can only be ignored.") next ctx
            | None ->
                return! (setStatusCode 404 >=> text "Not Found") next ctx
        })

    let taxRateHandler (taxYear: int) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<IIncomeDatabase>()
            
            match! database.GetTaxYearAsync session.UserId taxYear with
            | Some record ->
                let view =
                    { Error = None
                      Rate = record.TaxRate
                      TaxYear = record }
                    |> Views.taxRatePage
                    |> htmlView
                return! view next ctx
            | None ->
                return! (setStatusCode 404 >=> text $"Tax year {taxYear} not found.") next ctx
        })
        
    let executeTaxRateHandler (taxYear: int) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<IIncomeDatabase>()
            let! request = ctx.BindFormAsync<NewTaxRateRequest>()
            let! record = database.GetTaxYearAsync session.UserId taxYear
            
            match record, NewTaxRateRequest.Validate request with
            | Some record, Error err ->
                let view =
                    { Error = Some err
                      Rate = request.NewTaxRate
                      TaxYear = record }
                    |> Views.taxRatePage
                    |> htmlView
                return! view next ctx 
            | Some record, Ok request ->
                do! database.SetTaxYearRateAsync session.UserId record.TaxYear request.NewTaxRate
                return! (redirectTo false $"/income?year={record.TaxYear}") next ctx
            | None, _ ->
                return! (setStatusCode 404 >=> text $"Tax year {taxYear} not found.") next ctx
        })
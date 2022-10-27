namespace FoxyBalance.Server.Routes

open Giraffe
open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open FoxyBalance.Database.Interfaces
open FoxyBalance.Sync
open FoxyBalance.Server
open FoxyBalance.Server.Models
open FoxyBalance.Database.Models
open FoxyBalance.Server.Models.ViewModels
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

namespace FoxyBalance.Server.Routes

open Giraffe
open System
open FoxyBalance.Database.Interfaces
open FoxyBalance.Sync
open FoxyBalance.Server
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
            let! summary = database.SummarizeAsync session.UserId taxYear
            let summary = Option.defaultValue IncomeSummary.Default summary
                
            //let! count = database.CountAsync session.UserId status
            let count = Seq.length records
            let model : IncomeViewModel =
                { IncomeRecords = records
                  Summary = summary //{ summary with TotalEstimatedTax = summary.TotalNetShare * summary.TaxYear.TaxRate / 100 }
                  Page = page
                  TaxYear = taxYear
                  TotalPages = if count % limit > 0 then (count / limit) + 1 else count / limit
                  TotalRecordsForYear = count }
            
            return! htmlView (Views.homePage model) next ctx
        })

    let syncHandler : HttpHandler = 
        SyncShopifySalesViewModel.Default
        |> Views.Income.syncShopifySalesPage
        |> htmlView
        
    let executeSyncHandler : HttpHandler =
        RouteUtils.withSession(fun session next ctx -> task {
            let file = ctx.Request.Form.Files.GetFile "csvFile"
            
            if isNull file then
                let html =
                    { SyncShopifySalesViewModel.Default with Error = Some "Shopify earnings CSV file is required." }
                    |> Views.Income.syncShopifySalesPage
                    |> htmlView
                
                return! html next ctx
            else
                let database = ctx.GetService<IIncomeDatabase>()
                let gumroad = ctx.GetService<GumroadClient>()
                let parser = ctx.GetService<ShopifyPayoutParser>()
                let! importResult =
                    file.OpenReadStream()
                    |> parser.FromCsv
                    |> Seq.map (fun sale ->
                        {
                            Source = Shopify { TransactionId = sale.Id; Description = sale.Description }
                            SaleDate = sale.SaleDate
                            SaleAmount = sale.SaleAmount
                            PlatformFee = sale.ShopifyFee
                            ProcessingFee = sale.ProcessingFee
                            NetShare = sale.PartnerShare
                        })
                    |> database.ImportAsync session.UserId

                // TODO: determine which tax year to sync
                let! gumroadSales = gumroad.ListAllAsync(DateTimeOffset.Parse "2022-01-01")
                let! gumroadImportResult = 
                    gumroadSales
                    |> Seq.map (fun sale ->
                        {
                            Source = Gumroad { TransactionId = sale.Id; Description = sale.Description }
                            SaleDate = sale.CreatedAt
                            SaleAmount = sale.Price
                            PlatformFee = sale.GumroadFee
                            ProcessingFee = 0
                            NetShare = sale.Price - sale.GumroadFee 
                        })
                    |> database.ImportAsync session.UserId
                let totalNewRecords = importResult.TotalNewRecordsImported + gumroadImportResult.TotalNewRecordsImported

                return! redirectTo false $"/income?totalImported={totalNewRecords}" next ctx
        })

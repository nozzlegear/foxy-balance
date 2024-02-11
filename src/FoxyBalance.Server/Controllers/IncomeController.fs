namespace FoxyBalance.Server.Controllers

open FoxyBalance.BlazorViews
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open FoxyBalance.Server
open FoxyBalance.Server.Authentication
open FoxyBalance.Server.Extensions
open FoxyBalance.Server.Models.RequestModels
open FoxyBalance.Server.Models.ViewModels
open FoxyBalance.Sync
open FoxyBalance.Sync.Models
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Mvc
open System
open System.ComponentModel.DataAnnotations
open System.Threading.Tasks

[<Route("income")>]
type IncomeController(
    sessionUtil: ISessionLoaderUtil,
    incomeDatabase: IIncomeDatabase,
    partnerClient: ShopifyPartnerClient,
    paypalTransactionParser: PaypalTransactionParser,
    gumroadClient: GumroadClient
) =
    inherit Controller()

    [<HttpGet("")>]
    member this.GetHomePage(
        [<FromQuery; Range(1, Int32.MaxValue)>] ?page: int,
        [<FromQuery(Name = "year")>] ?taxYear: int
    ): Task<ExtendedActionResult> =
        // TODO: have asp.net auto-validate querystring instead of checking model validation state manually
        let page = if not (this.ModelState.IsValidField <@ fun _ -> page @>)
                   then 1
                   else defaultArg page 1
        let taxYear = defaultArg taxYear DateTimeOffset.UtcNow.Year
        let recordsPerPage = 35
        let session = sessionUtil.LoadSessionFromPrincipal(this.User)
        task {
            let! records = incomeDatabase.ListAsync session.UserId taxYear
            let! taxYears = incomeDatabase.ListTaxYearsAsync session.UserId
            let! summary = incomeDatabase.SummarizeAsync session.UserId taxYear
            let summary = Option.defaultValue IncomeSummary.Default summary
            let count = summary.TotalRecords

            return Blazor.Component<Components.Pages.Home>
                   |> Blazor.SetModelParameter<IncomeViewModel> {
                       IncomeRecords = records
                       Summary = summary
                       Page = page
                       TaxYear = taxYear
                       TaxYears = taxYears
                       TotalPages = if count % recordsPerPage > 0 then (count / recordsPerPage) + 1 else count / recordsPerPage
                       TotalRecordsForYear = count
                   }
                   |> ExtendedActionResult.BlazorView

            // htmlView (Views.homePage model) next ctx
        }

    [<HttpGet("sync")>]
    member this.GetSyncPage() =
        Blazor.Component<Components.Pages.Home>
        |> Blazor.SetModelParameter SyncShopifySalesViewModel.Default
        |> BlazorView
        // SyncShopifySalesViewModel.Default
        // |> Views.Income.syncShopifySalesPage
        // |> htmlView

    member private this.ImportShopifyTransactions() =
        let session = sessionUtil.LoadSessionFromPrincipal(this.User)

        let rec complete output (request: Task<ShopifyTransactionListResult>) = task {
            let! result = request
            // Combine this result's transactions with all other result's sales
            let output = Seq.append result.Transactions output
            
            match result.NextPageCursor with
            | Some cursor ->
                return!
                    Some cursor
                    |> partnerClient.ListTransactionsAsync
                    |> complete output 
            | None ->
                return output
        }
        
        task {
            let! transactions =
                partnerClient.ListTransactionsAsync None
                |> complete []

            return!
                transactions
                |> Seq.map (fun sale ->
                    {
                        Source = Shopify 
                            {
                                TransactionId = sale.Id
                                Description = sale.Description
                                CustomerDescription = sale.CustomerDescription
                            }
                        SaleDate = sale.TransactionDate
                        SaleAmount = sale.GrossAmount
                        PlatformFee = sale.ShopifyFee
                        ProcessingFee = sale.ProcessingFee
                        NetShare = sale.NetAmount
                    })
                |> incomeDatabase.ImportAsync session.UserId
    }

    member private this.ImportPaypalTransactions (file: IFormFile) = task {
        let session = sessionUtil.LoadSessionFromPrincipal(this.User)
        return!
            file.OpenReadStream()
            |> paypalTransactionParser.FromCsv
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
            |> incomeDatabase.ImportAsync session.UserId
    }

    [<NonAction>]
    member private this.ImportGumroadTransactions() =
        let session = sessionUtil.LoadSessionFromPrincipal(this.User)
        task {
            // TODO: determine which tax year to sync
            let! gumroadSales = gumroadClient.ListAllAsync(DateTimeOffset.Parse "2022-01-01")

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
                |> incomeDatabase.ImportAsync session.UserId
        }

    [<HttpPost("sync")>]
    member this.SyncIncomeTransactionsAsync(
        [<FromForm;>] ?syncGumroad: bool,
        [<FromForm;>] ?syncShopify: bool,
        [<FromForm(Name = "paypalCsvFile"); FileExtensions(Extensions = "csv")>] ?paypalCsvFile: FormFile
    ): Task<ExtendedActionResult> =
        task {
            // Import Shopify, Gumroad and Paypal transactions
            let! tasks = Task.WhenAll [
                if Option.isSome paypalCsvFile then
                    this.ImportPaypalTransactions(Option.get paypalCsvFile)

                if syncGumroad = Some true then
                    this.ImportGumroadTransactions()
                    
                if syncShopify = Some true then
                    this.ImportShopifyTransactions()
            ]
            let totalNewRecords = Seq.sumBy (_.TotalNewRecordsImported) tasks

            return RedirectToActionWithParams (nameof this.GetHomePage, nameof IncomeController, ["totalImported" => totalNewRecords])
        }

    [<HttpGet("new")>]
    member this.GetNewRecordPage(): ExtendedActionResult =
        Blazor.Component<Components.Pages.Home>
        |> Blazor.SetModelParameter NewIncomeRecordViewModel.Empty
        |> BlazorView
        // NewIncomeRecordViewModel.Empty
        // |> Views.createRecordPage
        // |> htmlView

    [<HttpPost("new")>]
    member this.CreateNewIncomeRecordAsync(
        [<FromForm; Required>] request: NewIncomeRecordRequest
    ): Task<ExtendedActionResult> =
        task {
            match NewIncomeRecordRequest.Validate request with
            | Result.Error err ->
                return Blazor.Component<Components.Pages.Home>
                       |> Blazor.SetModelParameter<NewIncomeRecordViewModel> (NewIncomeRecordViewModel.FromBadRequest request err)
                       |> BlazorView
                    // NewIncomeRecordViewModel.FromBadRequest request err
                    // |> Views.createRecordPage
                    // |> htmlView
            | Result.Ok partialRecord ->
                let session = sessionUtil.LoadSessionFromPrincipal(this.User)
                let! _ = incomeDatabase.ImportAsync session.UserId [partialRecord]
                return RedirectToActionWithParams (nameof this.GetHomePage, nameof IncomeController, ["totalImported" => 1])
        }

    [<HttpGet("{recordId}")>]
    member this.GetRecordDetailsPage(
        [<FromRoute; Required>] recordId: int64
    ): Task<ExtendedActionResult> =
        let session = sessionUtil.LoadSessionFromPrincipal(this.User)
        task {
            match! incomeDatabase.GetAsync session.UserId recordId with
            | Some record ->
                return Blazor.Component<Components.Pages.Home>
                       |> Blazor.SetModelParameter<IncomeRecordViewModel> { IncomeRecord = record }
                       |> BlazorView
                    // { IncomeRecord = record }
                    // |> Views.recordDetailsPage
            | None ->
                return ErrorView (404, $"Could not find an income record with id {recordId}.")
        }
        
    [<HttpGet("{recordId}/shopify-details.json")>]
    member this.RawShopifyTransactionHandler(
        [<FromRoute; Required>] recordId: int64
    ): Task<JsonResult> =
        let jsonError statusCode (message: string) =
            JsonResult({| ok = false; statusCode = statusCode; recordId = recordId; message = message |}, StatusCode = Nullable statusCode)
        let session = sessionUtil.LoadSessionFromPrincipal(this.User)
        task {
            match! incomeDatabase.GetAsync session.UserId recordId with
            | Some record ->
                match record.Source with
                | Shopify shopify ->
                    match! partnerClient.GetTransaction(shopify.TransactionId) with
                    | Some shopifyTransaction ->
                        return JsonResult({| transaction = shopifyTransaction; databaseRecord = record |})
                    | None ->
                        return jsonError 404 $"Shopify transaction could with id \"{shopify.TransactionId}\" could could not be found."
                | x ->
                    return jsonError StatusCodes.Status400BadRequest $"Record {recordId} is not a Shopify transaction, it is a {x.GetType().Name} transaction."
            | None ->
                return jsonError 404 $"Database record with id {recordId} not found."
        }

    [<HttpPost("{recordId}/ignore")>]
    member this.ToggleIgnoreRecordAsync(
        [<FromRoute; Required>] recordId: int64
    ): Task<ExtendedActionResult> =
        let session = sessionUtil.LoadSessionFromPrincipal(this.User)
        task {
            match! incomeDatabase.GetAsync session.UserId recordId with
            | Some record ->
                match record.Source with
                | ManualTransaction _ ->
                    return ErrorView (422, $"Manual transaction income records cannot be ignored, they can only be deleted.")
                | _ ->
                    do! incomeDatabase.SetIgnoreAsync session.UserId recordId (not record.Ignored)
                    return RedirectToActionWithParams (nameof this.GetRecordDetailsPage, nameof IncomeController, ["recordId" => recordId])
            | None ->
                return ErrorView (404, $"Income record with id {recordId} not found.")
        }

    [<HttpPost("{recordId}/delete")>]
    member this.DeleteRecordAsync(
        [<FromRoute; Required>] recordId: int64
    ): Task<ExtendedActionResult> =
        let session = sessionUtil.LoadSessionFromPrincipal(this.User)
        task {
            match! incomeDatabase.GetAsync session.UserId recordId with
            | Some record ->
                match record.Source with
                | ManualTransaction _ ->
                    do! incomeDatabase.DeleteAsync session.UserId recordId
                    return RedirectToActionWithParams(nameof this.GetHomePage, nameof IncomeController, ["year" => record.SaleDate.Year])
                | source ->
                    let formattedSource = Format.incomeSourceType source
                    return ErrorView(422, $"{formattedSource} income records cannot be deleted, they can only be ignored.")
            | None ->
                return ErrorView (404, $"Income record with id {recordId} not found.")
        }

    [<HttpGet("tax-rate/{taxYear}")>]
    member this.GetTaxRatePage (
        [<FromRoute; Required>] taxYear: int
    ): Task<ExtendedActionResult> =
        let session = sessionUtil.LoadSessionFromPrincipal(this.User)
        task {
            match! incomeDatabase.GetTaxYearAsync session.UserId taxYear with
            | Some record ->
                return Blazor.Component<Components.Pages.Home>
                       |> Blazor.SetModelParameter<TaxRateViewModel>
                         { Error = None
                           Rate = record.TaxRate
                           TaxYear = record }
                       |> BlazorView
                    // { Error = None
                    //   Rate = record.TaxRate
                    //   TaxYear = record }
                    // |> Views.taxRatePage
            | None ->
                return ErrorView (404, $"Tax Year {taxYear} not found.")
        }

    [<HttpPost("tax-rate/{taxYear}")>]
    member this.SetTaxRateForTaxYearAsync(
        [<FromRoute; Required>] taxYear: int,
        [<FromForm; Required>] request: NewTaxRateRequest
    ): Task<ExtendedActionResult> =
        let session = sessionUtil.LoadSessionFromPrincipal(this.User)
        task {
            let! record = incomeDatabase.GetTaxYearAsync session.UserId taxYear
            match record, NewTaxRateRequest.Validate request with
            | Some record, Error err ->
                return Blazor.Component<Components.Pages.Home>
                       |> Blazor.SetModelParameter<TaxRateViewModel>
                         { Error = Some err
                           Rate = request.NewTaxRate
                           TaxYear = record }
                       |> BlazorView
                    // { Error = Some err
                    //   Rate = request.NewTaxRate
                    //   TaxYear = record }
                    // |> Views.taxRatePage
            | Some record, Ok request ->
                do! incomeDatabase.SetTaxYearRateAsync session.UserId record.TaxYear request.NewTaxRate
                return RedirectToActionWithParams (nameof this.GetHomePage, nameof IncomeController, ["year" => record.TaxYear])
            | None, _ ->
                return ErrorView (404, $"Tax Year {taxYear} not found.")
        }

namespace FoxyBalance.Sync

open System
open System.Threading.Tasks
open FoxyBalance.Sync.Models
open JsonSharp
open Microsoft.Extensions.Options
open System.Net.Http
open Http.Query

type GumroadClient(options : IOptions<GumroadClientOptions>, httpClientFactory : IHttpClientFactory) =
    let options = options.Value
    let client = httpClientFactory.CreateClient("GumroadClient")
    let connect =
        Http.connect "https://api.gumroad.com"
        |> Http.client client
        |> Http.headers [
            "Accept", "application/json"
            "Authorization", $"Bearer {options.AccessToken}"
        ]
    
    let readSale (read: ElementReader): GumroadSale =
        let productName = read.string "product_name"
        let refunded = Option.defaultValue false (read.boolOrNone "refunded")
        let partiallyRefunded = Option.defaultValue false (read.boolOrNone "partially_refunded")
        let description = $"{productName}"
        {
            Id = read.string "id"
            ProductId = read.string "product_id"
            SellerId = read.string "seller_id"
            CreatedAt = DateTimeOffset.Parse (read.string "created_at")
            Price = read.int "price"
            GumroadFee = read.int "gumroad_fee"
            ChargedBack = read.bool "chargedback"
            OrderId = read.int64 "order_id"
            Refunded = refunded
            PartiallyRefunded = partiallyRefunded
            ProductName = productName
            CustomerEmail = read.string "purchase_email"
            Description = 
                if refunded then
                    $"(Refunded) {description}"
                else if partiallyRefunded then
                    $"(Partial Refund) {description}"
                else
                    description
            CustomerDescription =
                read.stringOrNone "purchase_email"
                |> Option.defaultValue "(unknown)"
        }
    
    let readSalesList (read: ElementReader): GumroadSaleList = 
        {
            Sales = read.array ("sales", readSale)
            NextPageUrl = read.stringOrNone "next_page_url"
            PreviousPageUrl = read.stringOrNone "previous_page_url"
        }

    member _.ListAsync (page: int, ?after: DateTimeOffset): Task<GumroadSaleList> =
        let after =
            after
            |> Option.map (fun after -> after.ToString("yyyy-MM-dd"))

        connect
        |> Http.method HttpMethod.Get
        |> Http.path "v2/sales"
        |> Http.query [
            "page" => string page
            "after" =?> after
        ]
        |> Http.executeJsonAsync readSalesList

    member _.ListAsync (page: string): Task<GumroadSaleList> =
        // The page here comes from Gumroad's list response and will contain a querystring.
        // It must be split into path and query.
        let path, query =
            match List.ofArray <| page.Split [|'?'|] with
            | path::[query] -> path, parse query
            | [path] -> path, List.empty
            | path::rest -> path, parse (Seq.head rest)
            | [] -> invalidArg "page" ""
        
        connect
        |> Http.method HttpMethod.Get
        |> Http.path path
        |> Http.query query
        |> Http.executeJsonAsync readSalesList

    member self.ListAllAsync (?after: DateTimeOffset): Task<GumroadSale seq> =
        let rec next (result: Task<GumroadSaleList>) (sales: GumroadSale seq) = task {
            let! result = result
            match result.NextPageUrl with
            | Some nextPageUrl ->
                let job = self.ListAsync(nextPageUrl)
                let sales = Seq.append sales result.Sales
                return! next job sales
            | None ->
                return sales
        }
            
        let job =
            match after with
            | Some after -> self.ListAsync(1, after) 
            | None -> self.ListAsync(1)
        
        next job Seq.empty

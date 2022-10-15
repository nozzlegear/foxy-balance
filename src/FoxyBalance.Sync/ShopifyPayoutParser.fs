namespace FoxyBalance.Sync

open System
open System.Text
open System.Globalization
open System.IO
open FSharp.Data
open FoxyBalance.Sync.Models

type Earnings = CsvProvider<"../../assets/shopify-earnings-example.csv">

type ShopifyPayoutParser() =
    let parseShopifyDate dt =
        DateTimeOffset.ParseExact(dt, "yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
        
    /// Creates a row identifier by Base64 encoding the category, sale date, sale amount, shop name and app title 
    let deriveRowIdentifier (row : Earnings.Row) =
        $"{row.Category}+{row.``Charge Creation Time``}+{row.``Partner Sale``}+{row.Shop}+{row.``App Title``}"
        |> Encoding.UTF8.GetBytes
        |> Convert.ToBase64String

    let parseShopifyAmount amt =
        int (amt * 100M)
    
    let parse (csv : Earnings) : ShopifySale seq =
        csv.Rows
        |> Seq.map (fun row -> {
            // Note: the csv contains a Charge ID column. This is NOT a transaction ID. It's a subscription charge ID,
            // which is erased in future versions of the csv if the customer cancels or changes their subscription. i.e.
            // just because a row's Charge ID isn't null now doesn't mean it won't be null in the future. Instead of
            // relying on that column, we derive a transaction ID by combining certain row values and encoding them.
            Id = deriveRowIdentifier row
            SaleDate = parseShopifyDate row.``Charge Creation Time``
            PayoutDate = parseShopifyDate row.``Payout Date``
            SaleAmount = parseShopifyAmount row.``Partner Sale``
            ShopifyFee = parseShopifyAmount row.``Shopify Fee``
            ProcessingFee = parseShopifyAmount row.``Processing Fee``
            PartnerShare = parseShopifyAmount row.``Partner Share``
            AppTitle = row.``App Title``
            Description = row.``Charge Type``
        })

    member _.FromCsv (fileText: string) =
        Earnings.Parse fileText
        |> parse
        
    member self.FromCsv (file : Stream) =
        Earnings.Load file
        |> parse

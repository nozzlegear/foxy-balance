namespace FoxyBalance.Sync

open System
open System.Net.Http
open System.Text
open System.Text.Json
open System.Threading
open System.Threading.Tasks
open JsonSharp
open ShopifySharp
open FoxyBalance.Sync.Models
open Microsoft.Extensions.Options
open ShopifySharp.Infrastructure
open Newtonsoft.Json.Linq

type private PartnerTransactionEdge = {
    Cursor: string
    Node: ShopifyTransaction
}

type private TransactionQueryParams = {
    query: string
    transactionTypes: string seq
    cursor: string option
}

type ShopifyPartnerClient(options: IOptions<ShopifyPartnerClientOptions>) =
    inherit GraphService("example.myshopify.com", options.Value.AccessToken)
    
    let options = options.Value
    let policy = LeakyBucketExecutionPolicy()

    do (
        base.SetExecutionPolicy(policy)
    )
    
    /// Reads a decimal and converts it to an integer amount
    let readToAmount (reader: ElementReader) (key: string): int =
        // The amount is in an object that looks like { "key": { "amount": "1.23" }}
        // However, charges created before a certain date in 2020 do not have a grossAmount or shopifyFee -- the objects
        // are null. It's assumed that Shopify just didn't record these values before that date. Default those values to
        // zero if they're null.
        match reader.tryGet key with
        | None ->
            0
        | Some parent ->
            let strValue = parent.string("amount")
            let decimal = Decimal.Parse(strValue)
            // Multiply the decimal by 100 to convert it to cents
            Convert.ToInt32(decimal * 100M)
            
    let readToApp (reader: ElementReader): ShopifyAppDetails option =
        reader.objectOrNone("app", fun app -> {
            Id = app.string "id"
            Title = app.string "name"
        })
        
    let describe (transaction: ShopifyTransaction): string =
        let appPrefix =
            Option.map (fun app -> app.Title) transaction.App
            |> Option.defaultValue "[Unknown App]"

        match transaction.Type with
        | AppSubscriptionSale -> $"{appPrefix} subscription"
        | AppSaleAdjustment -> $"{appPrefix} adjustment"
        | AppSaleCredit -> $"{appPrefix} credit"

    let mapTransactionEdge (edge: ElementReader): PartnerTransactionEdge = 
        let sale = edge.object("node", fun node ->
            let shop = node.get("shop")
            let shopName = shop.string "name"
            let toAmount = readToAmount node
            let grossAmount = toAmount "grossAmount"
            let shopifyFee = toAmount "shopifyFee"
            let netAmount = toAmount "netAmount"
            // Shopify does not directly include the processing fee, but it can be determined by subtracting the
            // net amount from gross amount and shopify fee.
            let processingFee = grossAmount - shopifyFee - netAmount
       
            {
                Id = node.string "id"
                TransactionDate = DateTimeOffset.Parse(node.string "createdAt")
                CustomerDescription = if String.IsNullOrWhiteSpace shopName then "REDACTED" else shopName
                GrossAmount = grossAmount
                ShopifyFee = shopifyFee
                ProcessingFee = processingFee
                NetAmount = netAmount
                App = readToApp node
                Type =
                    match node.string "__typename" with
                    | "AppSaleAdjustment" -> AppSaleAdjustment
                    | "AppSaleCredit" -> AppSaleCredit
                    | "AppSubscriptionSale" -> AppSubscriptionSale
                    | x -> raise (ArgumentOutOfRangeException($"Unhandled Shopify transaction type \"{x}\"", nameof x))
                // The description will be filled by the describe function after the rest of the node has been parsed
                Description = String.Empty
            }     
        )
        
        {
            Cursor = edge.string "cursor"
            Node = { sale with Description = describe sale }
        }

    /// Serializes the variables for a transaction query into a json string
    let serializeQuery (queryParams: TransactionQueryParams) =
        let variables = Map [
            // While enums are not strings in the graphql spec, they can be encoded as strings in the json request
            "types", box queryParams.transactionTypes
        ]
        let variables =
            match queryParams.cursor with
            | Some cursor -> Map.add "after" (box cursor) variables
            | _ -> variables
        
        let data = Map [
            "query", box queryParams.query
            "variables", box variables
        ]
        
        JsonSerializer.Serialize(data)
    
    let prepareRequest () : RequestUri =
        let ub = UriBuilder(
            "partners.shopify.com",
            Scheme = "https:",
            Port = 443,
            Path = $"{options.OrganizationId}/api/2023-01/graphql.json"
        )

        RequestUri(ub.Uri)
        
    
    
    override x.PostAsync(body: string, graphqlQueryCost: Nullable<int>, cancellationToken: CancellationToken):Task<JToken> =
        let request = prepareRequest()
        let content = new StringContent(body, Encoding.UTF8, "application/json")
        
        base.SendAsync(request, content, graphqlQueryCost, cancellationToken)
        
    override x.PostAsync(body: JToken, graphqlQueryCost: Nullable<int>, cancellationToken: CancellationToken): Task<JToken> =
        failwithf "not implemented"

    member x.ListTransactionsAsync (page: string option, ?cancellationToken): Task<ShopifyTransactionListResult> = task {
        let token = defaultArg cancellationToken CancellationToken.None
        let query = """ query listTransactions($types: [TransactionType!]!, $after: String) {
            transactions(types: $types, after: $after) {
                pageInfo {
                    hasNextPage
                    hasPreviousPage
                }
                edges {
                    cursor
                    node {
                        __typename
                        id
                        createdAt
                        ... on AppSubscriptionSale {
                            app {
                                id
                                name
                            }
                            shop {
                                name
                                myshopifyDomain
                            }
                            grossAmount {
                                amount
                            }
                            shopifyFee {
                                amount
                            }
                            netAmount {
                                amount
                            }
                        }
                        ... on AppSaleAdjustment {
                            app {
                                id
                                name
                            }
                            shop {
                                name
                                myshopifyDomain
                            }
                            grossAmount {
                                amount
                            }
                            shopifyFee {
                                amount
                            }
                            netAmount {
                                amount
                            }
                        }
                        ... on AppSaleCredit {
                            app {
                                id
                                name
                            }
                            shop {
                                name
                                myshopifyDomain
                            }
                            grossAmount {
                                amount
                            }
                            shopifyFee {
                                amount
                            }
                            netAmount {
                                amount
                            }
                        }
                    }
                }
            }
        }
        """
        
        let queryJson = serializeQuery {
            query = query
            transactionTypes = ["APP_SALE_ADJUSTMENT"; "APP_SALE_CREDIT"; "APP_SUBSCRIPTION_SALE"]
            cursor = page
        }
        let! result = x.PostAsync(queryJson, Nullable<int>(), token)
        
        // Parse the result into an ElementReader
        let document = ElementReader.parse(result.ToString()).get("transactions")
        
        let edges = document.array("edges", mapTransactionEdge)
        let pageInfo = document.get("pageInfo")
        
        let nextPageCursor =
            if pageInfo.bool "hasNextPage"
            then Some (Seq.last edges)
            else None
            
        let previousPageCursor =
            if pageInfo.bool "hasPreviousPage"
            then Some (Seq.head edges)
            else None
            
        let getCursor =
            Option.map (fun e -> e.Cursor)
        
        return {
            NextPageCursor = getCursor nextPageCursor
            PreviousPageCursor = getCursor previousPageCursor
            Transactions = edges |> Seq.map (fun e -> e.Node)
        }
    }
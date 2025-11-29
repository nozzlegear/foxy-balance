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

type private TransactionListParams = {
    query: string
    transactionTypes: string seq
    cursor: string option
}

type private TransactionGetParams = {
    query: string
    transactionId: string
}

type private TransactionQueryParams =
    | TransactionListParams of TransactionListParams
    | TransactionGetParams of TransactionGetParams

/// A ShopifySharp execution policy which retries requests after 750ms. The Shopify Partner API is hard limited to
/// four requests per second and does not conform to the GraphQL cost limits or REST leaky bucket limits.
type private PartnerServiceRetryExecutionPolicy () =
    let DELAY = TimeSpan.FromMilliseconds 750L

    let rec run (requestMessage: CloneableRequestMessage, execute: ExecuteRequestAsync<_>, cancellationToken: CancellationToken) = task {
        try
            return! execute.Invoke requestMessage
        with
        :? ShopifyRateLimitException ->
            // Delay execution and then retry
            do! Task.Delay(DELAY, cancellationToken)
            use! requestMessage = requestMessage.CloneAsync()
            return! run (requestMessage, execute, cancellationToken)
    }

    interface IRequestExecutionPolicy with
        member this.Run(requestMessage, executeRequestAsync, cancellationToken, _) =
            run(requestMessage, executeRequestAsync, cancellationToken)
    end

type ShopifyPartnerClient(
    options: IOptions<ShopifyPartnerClientOptions>,
    retryPolicy: IRequestExecutionPolicy,
    httpClientFactory: IHttpClientFactory
) =
    inherit PartnerService(options.Value.OrganizationId, options.Value.AccessToken)

    let httpClient = httpClientFactory.CreateClient()

    do (
        base.SetExecutionPolicy(retryPolicy)
        base.SetHttpClient(httpClient)
    )

    let options = options.Value

    let [<Literal>] TransactionProperties = """
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
    """

    let TransactionTypesQuery = $"""
        __typename
        id
        createdAt
        ... on AppSubscriptionSale {{
            {TransactionProperties}
        }}
        ... on AppSaleAdjustment {{
            {TransactionProperties}
        }}
        ... on AppSaleCredit {{
            {TransactionProperties}
        }}
    """

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
        | AppSubscriptionSale -> $"Subscription to {appPrefix}"
        | AppSaleAdjustment -> $"Adjustment to {appPrefix} subscription"
        | AppSaleCredit -> $"Credit for {appPrefix} subscription"

    let mapTransaction (node: ElementReader): ShopifyTransaction =
        let shop = node.get("shop")
        let shopName = shop.string "name"
        let transactionType =
            match node.string "__typename" with
            | "AppSaleAdjustment" -> AppSaleAdjustment
            | "AppSaleCredit" -> AppSaleCredit
            | "AppSubscriptionSale" -> AppSubscriptionSale
            | x -> raise (ArgumentOutOfRangeException($"Unhandled Shopify transaction type \"{x}\"", nameof x))
        let toAmount = readToAmount node
        let grossAmount = toAmount "grossAmount"
        let shopifyFee = toAmount "shopifyFee"
        let netAmount = toAmount "netAmount"
        // Shopify only started recording the grossAmount at a certain point in time, before which the grossAmount is always zero.
        // If this is the case, the grossAmount should copy its value from the netAmount -- but only if this is an AppSubscriptionSale
        let grossAmount =
            match grossAmount, transactionType with
            | 0, AppSubscriptionSale -> netAmount
            | x, _ -> x
        // Shopify does not directly include the processing fee, but it can be determined by subtracting the
        // net amount from gross amount and shopify fee.
        let processingFee = grossAmount - shopifyFee - netAmount

        let transaction: ShopifyTransaction = {
            Id = node.string "id"
            TransactionDate = DateTimeOffset.Parse(node.string "createdAt")
            CustomerDescription = if String.IsNullOrWhiteSpace shopName then "REDACTED" else shopName
            Type = transactionType
            GrossAmount = grossAmount
            ShopifyFee = shopifyFee
            ProcessingFee = processingFee
            NetAmount = netAmount
            App = readToApp node
            // The description will be filled by the describe function after the rest of the node has been parsed
            Description = String.Empty
        }

        { transaction with Description = describe transaction }

    let mapTransactionEdge (edge: ElementReader): PartnerTransactionEdge =
        let sale = edge.object("node", mapTransaction)

        {
            Cursor = edge.string "cursor"
            Node = { sale with Description = describe sale }
        }

    /// Serializes the variables for a transaction query into a json string
    let serializeQuery (queryParams: TransactionQueryParams) =
        let data: Map<string, obj> =
            match queryParams with
            | TransactionListParams queryParams ->
                let variables = Map [
                    // While enums are not strings in the graphql spec, they can be encoded as strings in the json request
                    // Must match the name of the variable used in the graph query
                    "types", box queryParams.transactionTypes
                ]
                let variables =
                    // Must match the name of the variable used in the graph query
                    match queryParams.cursor with
                    | Some cursor -> Map.add "after" (box cursor) variables
                    | _ -> variables

                Map [
                    "query", box queryParams.query
                    "variables", box variables
                ]
            | TransactionGetParams queryParams ->
                let variables = Map [
                    // Must match the name of the variable used in the graph query
                    "transactionId", queryParams.transactionId
                ]

                Map [
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

    override x.PostAsync(body: string, cancellationToken: CancellationToken):Task<JToken> =
        // Cannot use base.BuildRequestUri because PartnerService does not override it, so it drops
        // down to the ShopifyService implementation which uses the wrong domain and path
        let uri = Uri($"https://partners.shopify.com/{options.OrganizationId}/api/{base.APIVersion}/graphql.json")
                  |> RequestUri
        let content = new StringContent(body, Encoding.UTF8, "application/json")
        let job = base.ExecuteRequestAsync(uri, HttpMethod.Post, cancellationToken, content)

        task {
            let! outcome = job
            return outcome.Result.["data"]
        }

    member x.ListTransactionsAsync (page: string option, ?cancellationToken): Task<ShopifyTransactionListResult> = task {
        let token = defaultArg cancellationToken CancellationToken.None
        let query = $"""
            query listTransactions($types: [TransactionType!]!, $after: String) {{
                transactions(types: $types, after: $after) {{
                    pageInfo {{
                        hasNextPage
                        hasPreviousPage
                    }}
                    edges {{
                        cursor
                        node {{
                            {TransactionTypesQuery}
                        }}
                    }}
                }}
            }}
        """

        let queryJson =
            { query = query
              transactionTypes = ["APP_SALE_ADJUSTMENT"; "APP_SALE_CREDIT"; "APP_SUBSCRIPTION_SALE"]
              cursor = page }
            |> TransactionListParams
            |> serializeQuery
        let! result = x.PostAsync(queryJson, token)

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

    member x.GetTransaction (transactionId: string, ?cancellationToken): Task<ShopifyTransaction option> = task {
        let token = defaultArg cancellationToken CancellationToken.None
        let query = $"""
            query getTransaction($transactionId: ID!) {{
                transaction(id: $transactionId) {{
                    {TransactionTypesQuery}
                }}
            }}
        """
        let queryJson =
            { query = query
              transactionId = transactionId }
            |> TransactionGetParams
            |> serializeQuery
        let! result = x.PostAsync(queryJson, token)

        // Parse the result into an ElementReader
        let document = ElementReader.parse(result.ToString())

        return document.objectOrNone("transaction", mapTransaction)
    }

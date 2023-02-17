namespace FoxyBalance.Sync.Models

open System

type GumroadClientOptions() =
    member val AccessToken: string = "" with get, set
    member val ApplicationId: string = "" with get, set
    member val ApplicationSecret: string = "" with get, set
    
type ShopifyPartnerClientOptions() =
    member val AccessToken: string = "" with get, set
    member val OrganizationId: string = "" with get, set
    
type ShopifyAppDetails = {
    Id: string
    Title: string
}

type ShopifyTransactionType =
    | AppSubscriptionSale
    | AppSaleAdjustment
    | AppSaleCredit

type ShopifyTransaction = {
    Id: string
    Type: ShopifyTransactionType
    TransactionDate: DateTimeOffset
    CustomerDescription: string
    Description: string
    GrossAmount: int
    ShopifyFee: int
    ProcessingFee: int
    NetAmount: int
    App: ShopifyAppDetails option
}

type GumroadSale = {
    Id: string
    ProductId: string
    ProductName: string
    SellerId: string
    CreatedAt: DateTimeOffset
    Price: int
    GumroadFee: int
    Refunded: bool
    PartiallyRefunded: bool
    ChargedBack: bool
    OrderId: int64
    CustomerEmail: string
    Description: string
    CustomerDescription: string
}

type GumroadSaleList = {
    NextPageUrl: string option
    PreviousPageUrl: string option
    Sales: GumroadSale seq
}

type PaypalInvoice = {
    Id: string
    DateCreated: DateTimeOffset
    Customer: string
    Gross: int
    Discount: int
    Fee: int
    Net: int
    InvoiceNumber: string
    Description: string
    CustomerDescription: string
}

type ShopifyTransactionListResult = {
    NextPageCursor: string option
    PreviousPageCursor: string option
    Transactions: ShopifyTransaction seq
}
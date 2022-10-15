namespace FoxyBalance.Sync.Models

open System

type GumroadClientOptions() =
    member val AccessToken: string = "" with get, set
    member val ApplicationId: string = "" with get, set
    member val ApplicationSecret: string = "" with get, set
    
type ShopifySale = {
    Id : string
    SaleDate : DateTimeOffset
    PayoutDate : DateTimeOffset
    SaleAmount : int
    ShopifyFee : int
    ProcessingFee : int
    PartnerShare : int
    AppTitle : string
    Description : string
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
}

type GumroadSaleList = {
    NextPageUrl: string option
    PreviousPageUrl: string option
    Sales: GumroadSale seq
}

namespace FoxyBalance.Database.Models

open System

type IDatabaseOptions =
    abstract member ConnectionString : string with get

type EmailAddress = string
type UserId = int
type TransactionId = int64
type IncomeId = int64

type UserIdentifier =
    | Id of UserId
    | Email of EmailAddress

type User =
    { Id : UserId
      EmailAddress : EmailAddress
      DateCreated : DateTimeOffset
      HashedPassword : string }
    
type PartialUser =
    { EmailAddress : EmailAddress
      HashedPassword : string }
    
type TransactionStatus =
    | Pending
    | Cleared of DateTimeOffset
    
type CheckDetails =
    { CheckNumber : string }
    
type BillDetails =
    { Recurring : bool }
    
type TransactionType =
    | Check of CheckDetails
    | Bill of BillDetails
    | Credit 
    | Debit 
    
type Transaction =
    { Id : TransactionId
      Name : string
      Amount : decimal
      DateCreated : DateTimeOffset
      Status : TransactionStatus
      Type : TransactionType }
    
type PartialTransaction =
    { Name : string
      DateCreated : DateTimeOffset
      Amount : decimal
      Status : TransactionStatus
      Type : TransactionType }
    
type TransactionSum =
    { Sum : decimal
      PendingSum : decimal
      ClearedSum : decimal
      PendingDebitSum : decimal
      ClearedDebitSum : decimal
      PendingCreditSum : decimal
      ClearedCreditSum : decimal }
    
type TaxYear = {
    Id : int
    TaxYear : int
    TaxRate : decimal
}

type IncomeSource =
    | Gumroad of gumroadId : string
    | Shopify of shopifyId : string
    | Paypal of paypalId : string
    | Stripe of stripeId : string
    | ManualTransaction of description : string

type IncomeRecord =
    { Id : IncomeId
      Source: IncomeSource
      SaleDate : DateTimeOffset
      SaleAmount : decimal
      PlatformFee : decimal
      ProcessingFee : decimal
      NetShare : decimal
      EstimatedTax : decimal
      // Indicates that the income record is ignored and not counted as income when estimating taxes. Useful for cases where the app
      // syncs income from external sources that were eventually refunded or were otherwise not applicable for tax purposes.
      Ignored : bool }

type Order =
    | Ascending
    | Descending

type StatusFilter =
    | AllTransactions
    | PendingTransactions
    | ClearedTransactions

type ListOptions =
    { Limit : int
      Offset : int
      Order : Order
      Status : StatusFilter }

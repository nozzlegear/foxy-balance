namespace FoxyBalance.Database.Models

open System

type IDatabaseOptions =
    abstract member ConnectionString : string with get

type EmailAddress = string
type UserId = int
type TransactionId = int64
type IncomeId = int64
type TaxYearId = int

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
    
type TaxYear =
    { Id : TaxYearId
      TaxYear : int
      TaxRate : int }
    
type IncomeSourceDescription =
    { TransactionId : string
      CustomerDescription : string
      Description : string }
    
type ManualIncomeSourceDescription =
    { Description : string
      CustomerDescription : string option }

type IncomeSource =
    | Gumroad of IncomeSourceDescription
    | Shopify of IncomeSourceDescription
    | Paypal of IncomeSourceDescription
    | Stripe of IncomeSourceDescription
    | ManualTransaction of ManualIncomeSourceDescription

type IncomeRecord =
    { Id : IncomeId
      Source: IncomeSource
      SaleDate : DateTimeOffset
      SaleAmount : int
      PlatformFee : int
      ProcessingFee : int
      NetShare : int
      EstimatedTax : int
      // Indicates that the income record is ignored and not counted as income when estimating taxes. Useful for cases where the app
      // syncs income from external sources that were eventually refunded or were otherwise not applicable for tax purposes.
      Ignored : bool }
    
type PartialIncomeRecord =
    { Source : IncomeSource
      SaleDate : DateTimeOffset
      SaleAmount : int
      PlatformFee : int
      ProcessingFee : int
      NetShare : int }

type IncomeSummary =
    { TaxYear : TaxYear
      TotalRecords : int
      TotalSales : int
      TotalFees : int
      TotalNetShare : int
      TotalEstimatedTax : int }
    with
    static member Default =
        { TaxYear =
            { Id = 0
              TaxYear = DateTimeOffset.UtcNow.Year
              TaxRate = 33 }
          TotalRecords = 0
          TotalSales = 0
          TotalFees = 0
          TotalNetShare = 0
          TotalEstimatedTax = 0 }
    
type IncomeImportSummary =
    { TotalNewRecordsImported : int
      TotalSalesImported : int
      TotalFeesImported : int
      TotalNetShareImported : int
      TotalEstimatedTaxesImported : decimal }

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
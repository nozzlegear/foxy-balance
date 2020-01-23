namespace FoxyBalance.Database.Models

open System
open System

type IDatabaseOptions =
    abstract member ConnectionString : string with get

type EmailAddress = string
type UserId = int
type TransactionId = int64

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

namespace FoxyBalance.Database.Models

open System

type EmailAddress = string
type UserId = int
type TransactionId = int

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
    | PendingWithExpectedChargeDate of DateTimeOffset
    | Completed of DateTimeOffset
    
type CheckDetails =
    { CheckNumber : string }
    
type BillDetails =
    { Recurring : bool }
    
type TransactionDetails =
    | CheckDetails of CheckDetails
    | BillDetails of BillDetails
    | None 
    
type Transaction =
    { Id : TransactionId
      Name : string
      Amount : decimal
      DateCreated : DateTimeOffset
      Status : TransactionStatus
      Details : TransactionDetails }
    
type PartialTransaction =
    { Name : string
      Amount : decimal
      Status : TransactionStatus
      Details : TransactionDetails }
    
type Limit =
    | Limit of int
    
type Offset =
    | Offset of int 

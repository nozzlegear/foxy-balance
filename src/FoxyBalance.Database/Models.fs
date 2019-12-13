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
    
type TransactionStatus =
    | Pending 
    | PendingWithExpectedChargeDate of DateTimeOffset
    | Completed of DateTimeOffset
    
type Bill =
    { Id : TransactionId
      Name : string
      Amount : decimal
      DateCreated : DateTimeOffset
      Status : TransactionStatus }
    
type Check =
    { Id : TransactionId
      Name : string
      Amount : decimal
      DateCreated : DateTimeOffset
      Status : TransactionStatus
      CheckNumber : string }

type Charge =
    { Id : TransactionId
      Name : string
      Amount : decimal
      DateCreated : DateTimeOffset
      Status : TransactionStatus }

type PartialUser =
    { EmailAddress : EmailAddress
      HashedPassword : string }
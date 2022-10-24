namespace FoxyBalance.Server.Models

open System.Globalization
open FoxyBalance.Database.Models
open Microsoft.Extensions.Configuration

type IConstants =
    abstract member HashingKey : string with get
    abstract member ConnectionString : string with get
    
type Constants(config : IConfiguration) =
    let get key =
        match config.Item key with
        | null
        | "" -> failwithf "Constant %s was not found in configuration settings." key
        | x -> x 
    let connStr key =
        match config.GetConnectionString key with
        | null
        | "" -> failwithf "Connection String %s was not found in configuration settings." key
        | x -> x
        
    interface IConstants with
        member val HashingKey = get "HashingKey"
        member val ConnectionString = connStr "SqlDatabase"
        
type DatabaseOptions(constants : IConstants) =
    interface FoxyBalance.Database.Models.IDatabaseOptions with
        member val ConnectionString = constants.ConnectionString
    
type Session =
    { UserId : int }
     
module RequestModels =
    [<CLIMutable>]
    type LoginRequest =
        { Username : string
          Password : string }
        
    [<CLIMutable>]
    type EditTransactionRequest =
        { Amount : string
          Name : string
          CheckNumber : string
          Date : string
          ClearDate : string
          TransactionType : string }
        with
        static member Validate model : Result<PartialTransaction, string> =
            let tryParseDate dateStr =
                System.DateTimeOffset.TryParseExact(
                    dateStr,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None)
                
            let validateName (output : PartialTransaction) =
                if String.isEmpty model.Name then
                    Error "You must enter a name or description for this transaction."
                else
                    Ok { output with Name = model.Name }
                    
            let validateAmount (output : PartialTransaction) =
                // The Amount must be parsed to a decimal, and must be greater than 0.01
                match System.Decimal.TryParse model.Amount with
                | false, _ ->
                    Error (sprintf "Could not parse %s to a number or decimal." model.Amount)
                | true, amount when amount < 0.01M ->
                    Error "Amount must be greater than 0.01."
                | true, amount when amount % 0.01M <> 0M ->
                    // The amount has more than two decimal places
                    Error "Amount cannot have more than two decimal places."
                | true, amount ->
                    Ok { output with Amount = amount }
                    
            let validateDateCreated (output : PartialTransaction) = 
                match tryParseDate model.Date with
                | false, _ ->
                    Error (sprintf "You must enter a valid transaction date. Received %s which could not be parsed." model.Date)
                | true, date ->
                    Ok { output with DateCreated = date }
                    
            let validateType (output : PartialTransaction) =
                match model.TransactionType, model.CheckNumber with
                | "credit", _ ->
                    Ok { output with Type = Credit }
                | "debit", String.NotEmpty x ->
                    Ok { output with Type = Check { CheckNumber = model.CheckNumber } }
                | "debit", String.EmptyOrWhitespace ->
                    Ok { output with Type = Debit }
                | x, _ ->
                    Error (sprintf "Unrecognized transaction type %s." x)
                    
            let validateStatus (output : PartialTransaction) =
                match model.ClearDate, tryParseDate model.ClearDate with
                | String.EmptyOrWhitespace, _ ->
                    Ok { output with Status = Pending }
                | String.NotEmpty _, (false, _) ->
                    Error (sprintf "Unable to parse transaction's clear date. Received %s." model.ClearDate)
                | String.NotEmpty _, (true, clearDate) ->
                    Ok { output with Status = Cleared clearDate }
            
            // Create a default PartialTransaction, then modify it while validating the request
            { Name = System.String.Empty
              Amount = 0.00M
              DateCreated = System.DateTimeOffset.Now
              Type = Debit
              Status = Pending }
            |> Result.Ok
            |> Result.bind validateName 
            |> Result.bind validateAmount
            |> Result.bind validateDateCreated
            |> Result.bind validateType
            |> Result.bind validateStatus 
    
module ViewModels =
    type RouteType =
        | Balance
        | Income
    
    type PaginationOptions =
        { StatusFilter: StatusFilter
          CurrentPage: int
          MaxPages: int
          RouteType: RouteType }
    
    type LoginViewModel =
        { Error : string option
          Username : string option }
        
    type RegisterViewModel =
        { Error : string option
          Username : string option }
        
    type HomePageViewModel =
        { Transactions : Transaction seq
          Sum : TransactionSum
          Page : int
          TotalPages : int
          TotalTransactions : int
          Status : StatusFilter }
    
    type IncomeViewModel =
        { IncomeRecords : IncomeRecord seq
          Summary : IncomeSummary
          TaxYear : int
          Page : int
          TotalPages : int
          TotalRecordsForYear : int }
    
    type EditTransactionViewModel =
        { Error : string option
          Type : string
          Amount : string
          ClearDate : string 
          CheckNumber : string
          DateCreated : string
          Name : string }
        with
        static member FromBadRequest (r : RequestModels.EditTransactionRequest) msg : EditTransactionViewModel =
            let empty = System.String.Empty
            { Error = Some msg
              Amount = String.defaultValue empty r.Amount
              ClearDate = String.defaultValue empty r.ClearDate
              CheckNumber = String.defaultValue empty r.CheckNumber
              DateCreated = String.defaultValue empty r.Date
              Type = 
                match r.TransactionType with
                | "credit" -> "credit"
                | _ -> "debit"
              Name = String.defaultValue empty r.Name }
        static member FromExistingTransaction (t : Transaction) =
            let empty = System.String.Empty
            let clearDate =
                match t.Status with
                | Cleared cleared ->
                    Format.date cleared
                | Pending ->
                    empty
            let checkNumber =
                match t.Type with
                | Check check ->
                    check.CheckNumber
                | _ ->
                    empty
            
            { Error = None
              Amount = Format.amount t.Amount
              ClearDate = clearDate
              CheckNumber = checkNumber
              DateCreated = Format.date t.DateCreated
              Type = Format.transactionType t.Type
              Name = t.Name }
        static member Default =
            { Error = None
              Amount = ""
              ClearDate = ""
              CheckNumber = ""
              DateCreated = ""
              Type = "debit"
              Name = "" }
            
    type TransactionViewModel =
        | NewTransaction of EditTransactionViewModel
        | ExistingTransaction of int64 * EditTransactionViewModel
        
    type SyncShopifySalesViewModel =
        { Error : string option
          SyncGumroadIncome : bool
          SyncPayPalInvoices : bool
          ShopifyFileWasReset : bool }
        with
        static member Default =
            { Error = None
              SyncGumroadIncome = true
              SyncPayPalInvoices = true
              ShopifyFileWasReset = false }
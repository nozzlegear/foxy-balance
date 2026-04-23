namespace FoxyBalance.CLI.Commands

open System
open FSharp.SystemCommandLine
open FSharp.SystemCommandLine.Input
open FoxyBalance.CLI
open FoxyBalance.CLI.Domain
open FoxyBalance.CLI.TokenStore
open FoxyBalance.CLI.Formatters

module Transactions =

    /// `transactions list`: GET /api/v1/transactions
    let listCommand : System.CommandLine.Command =
        let pageOpt = option<int> "--page" |> desc "Page number (default: 1)" |> defaultValue 1
        let statusOpt = optionMaybe<string> "--status" |> desc "Filter by status: pending, cleared, all (default: all)"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON" |> defaultValue false

        let action (page: int, status: string option, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)

                let queryParams =
                    [ sprintf "page=%d" page
                      match status with
                      | Some s when not (String.IsNullOrWhiteSpace s) -> sprintf "status=%s" s
                      | _ -> () ]
                    |> String.concat "&"

                let path = sprintf "/api/v1/transactions?%s" queryParams
                let! result = client.GetCollectionAsync<TransactionDto>(path)

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok transactions ->
                    if json then
                        printJson transactions
                    else
                        printTransactions transactions
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "list" {
            description "List transactions with pagination and filtering"
            inputs (pageOpt, statusOpt, jsonOpt)
            setAction action
        }

    /// `transactions view <id>`: GET /api/v1/transactions/{id}
    let viewCommand : System.CommandLine.Command =
        let idArg = argument<int64> "id" |> desc "Transaction ID"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON" |> defaultValue false

        let action (id: int64, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let! result = client.GetAsync<TransactionDto>(sprintf "/api/v1/transactions/%d" id)

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok t ->
                    if json then
                        printJson t
                    else
                        printTransaction t
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "view" {
            description "View a single transaction"
            inputs (idArg, jsonOpt)
            setAction action
        }

    /// `transactions create`: POST /api/v1/transactions
    let createCommand : System.CommandLine.Command =
        let nameOpt = optionMaybe<string> "--name" |> desc "Transaction name or description"
        let amountOpt = optionMaybe<string> "--amount" |> desc "Transaction amount (e.g. 50.00)"
        let dateOpt = optionMaybe<string> "--date" |> desc "Transaction date (yyyy-MM-dd)"
        let typeOpt = option<string> "--type" |> desc "Transaction type: debit, credit, check" |> defaultValue "debit"
        let checkNumberOpt = optionMaybe<string> "--check-number" |> desc "Check number (required if type is check)"
        let clearDateOpt = optionMaybe<string> "--clear-date" |> desc "Clear date (yyyy-MM-dd)"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON" |> defaultValue false

        let action (name: string option, amount: string option, date: string option, txType: string, checkNumber: string option, clearDate: string option, json: bool) =
            async {
                let resolvedName =
                    match name with
                    | Some n when not (String.IsNullOrWhiteSpace n) -> n
                    | _ ->
                        printf "Name: "
                        Console.ReadLine()

                let resolvedAmount =
                    match amount with
                    | Some a when not (String.IsNullOrWhiteSpace a) -> a
                    | _ ->
                        printf "Amount: "
                        Console.ReadLine()

                let resolvedDate =
                    match date with
                    | Some d when not (String.IsNullOrWhiteSpace d) -> d
                    | _ ->
                        printf "Date (yyyy-MM-dd): "
                        Console.ReadLine()

                let resolvedCheckNumber =
                    match checkNumber with
                    | Some cn when not (String.IsNullOrWhiteSpace cn) -> cn
                    | _ ->
                        if txType = "check" then
                            printf "Check number: "
                            Console.ReadLine()
                        else
                            ""

                let request: ApiTransactionRequest =
                    { Name = resolvedName
                      Amount = resolvedAmount
                      Date = resolvedDate
                      ClearDate = defaultArg clearDate ""
                      TransactionType = txType
                      CheckNumber = resolvedCheckNumber }

                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let! result = client.PostAsync<TransactionDto>("/api/v1/transactions", request)

                match result with
                | Error e ->
                    printfn "Error creating transaction: %s" e
                    return ExitCodes.generalError
                | Ok t ->
                    if json then
                        printJson t
                    else
                        printTransaction t
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "create" {
            description "Create a new transaction"
            inputs (nameOpt, amountOpt, dateOpt, typeOpt, checkNumberOpt, clearDateOpt, jsonOpt)
            setAction action
        }

    /// `transactions update <id>`: PUT /api/v1/transactions/{id}
    let updateCommand : System.CommandLine.Command =
        let idArg = argument<int64> "id" |> desc "Transaction ID"
        let nameOpt = optionMaybe<string> "--name" |> desc "Transaction name"
        let amountOpt = optionMaybe<string> "--amount" |> desc "Transaction amount"
        let dateOpt = optionMaybe<string> "--date" |> desc "Transaction date (yyyy-MM-dd)"
        let typeOpt = optionMaybe<string> "--type" |> desc "Transaction type: debit, credit, check"
        let checkNumberOpt = optionMaybe<string> "--check-number" |> desc "Check number"
        let clearDateOpt = optionMaybe<string> "--clear-date" |> desc "Clear date (yyyy-MM-dd)"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON" |> defaultValue false

        let action (id: int64, name: string option, amount: string option, date: string option, txType: string option, checkNumber: string option, clearDate: string option, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let! existing = client.GetAsync<TransactionDto>(sprintf "/api/v1/transactions/%d" id)

                match existing with
                | Error e ->
                    printfn "Error fetching transaction: %s" e
                    return ExitCodes.generalError
                | Ok existingTx ->
                    let request: ApiTransactionRequest =
                        { Name =
                            match name with
                            | Some n when not (String.IsNullOrWhiteSpace n) -> n
                            | _ -> existingTx.Name
                          Amount =
                            match amount with
                            | Some a when not (String.IsNullOrWhiteSpace a) -> a
                            | _ -> string existingTx.Amount
                          Date =
                            match date with
                            | Some d when not (String.IsNullOrWhiteSpace d) -> d
                            | _ -> existingTx.DateCreated.ToString("yyyy-MM-dd")
                          ClearDate =
                            match clearDate with
                            | Some cd when not (String.IsNullOrWhiteSpace cd) -> cd
                            | _ ->
                                match existingTx.ClearDate with
                                | Some d -> d.ToString("yyyy-MM-dd")
                                | None -> ""
                          TransactionType =
                            match txType with
                            | Some t when not (String.IsNullOrWhiteSpace t) -> t
                            | _ -> existingTx.Type
                          CheckNumber = defaultArg checkNumber "" }

                    let! result = client.PutAsync<TransactionDto>(sprintf "/api/v1/transactions/%d" id, request)

                    match result with
                    | Error e ->
                        printfn "Error updating transaction: %s" e
                        return ExitCodes.generalError
                    | Ok t ->
                        if json then
                            printJson t
                        else
                            printTransaction t
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "update" {
            description "Update an existing transaction"
            inputs (idArg, nameOpt, amountOpt, dateOpt, typeOpt, checkNumberOpt, clearDateOpt, jsonOpt)
            setAction action
        }

    /// `transactions delete <id>`: DELETE /api/v1/transactions/{id}
    let deleteCommand : System.CommandLine.Command =
        let idArg = argument<int64> "id" |> desc "Transaction ID"
        let forceOpt = option<bool> "--force" |> alias "-f" |> desc "Skip confirmation prompt" |> defaultValue false

        let action (id: int64, force: bool) =
            async {
                let confirmed =
                    if force then true
                    else
                        printf "Are you sure you want to delete transaction %d? [y/N] " id
                        let response = Console.ReadLine()
                        response.Equals("y", StringComparison.OrdinalIgnoreCase)
                        || response.Equals("yes", StringComparison.OrdinalIgnoreCase)

                if not confirmed then
                    printfn "Cancelled."
                    return ExitCodes.success
                else
                    let baseUrl = getBaseUrl ()
                    let client = FoxyBalanceClient(baseUrl)
                    let! result = client.DeleteAsync(sprintf "/api/v1/transactions/%d" id)
                    match result with
                    | Error e ->
                        printfn "Error: %s" e
                        return ExitCodes.generalError
                    | Ok () ->
                        printfn "Transaction %d deleted." id
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "delete" {
            description "Delete a transaction"
            inputs (idArg, forceOpt)
            setAction action
        }

    /// `transactions import`: POST /api/v1/transactions/import
    let importCommand : System.CommandLine.Command =
        let formatOpt = option<string> "--format" |> desc "Import format (capital-one)" |> defaultValue "capital-one"
        let fileOpt = optionMaybe<string> "--file" |> desc "Path to CSV file"

        let action (format: string, file: string option) =
            async {
                match file with
                | None ->
                    printfn "Error: --file is required"
                    return ExitCodes.generalError
                | Some f when not (IO.File.Exists f) ->
                    printfn "Error: File not found: %s" f
                    return ExitCodes.generalError
                | Some f ->
                    let csvContent = IO.File.ReadAllText(f)
                    let request: ApiBulkImportRequest =
                        { Format = format
                          Transactions = csvContent }

                    let baseUrl = getBaseUrl ()
                    let client = FoxyBalanceClient(baseUrl)
                    let! result = client.PostAsync<ImportResultDto>("/api/v1/transactions/import", request)

                    match result with
                    | Error e ->
                        printfn "Error importing transactions: %s" e
                        return ExitCodes.generalError
                    | Ok importResult ->
                        printImportResult importResult
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "import" {
            description "Import transactions from a CSV file"
            inputs (formatOpt, fileOpt)
            setAction action
        }

    /// Build the `transactions` parent command.
    let buildCommand : System.CommandLine.Command =
        command "transactions" {
            description "Manage transactions"
            noAction
            addCommands [ listCommand; viewCommand; createCommand; updateCommand; deleteCommand; importCommand ]
        }

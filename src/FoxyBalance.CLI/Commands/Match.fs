namespace FoxyBalance.CLI.Commands

open System
open FSharp.SystemCommandLine
open FSharp.SystemCommandLine.Input
open FoxyBalance.CLI
open FoxyBalance.CLI.Domain
open FoxyBalance.CLI.TokenStore
open FoxyBalance.CLI.Formatters

module Match =

    /// `match suggestions`: GET /api/v1/bills/match/suggestions
    let suggestionsCommand : System.CommandLine.Command =
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON" |> defaultValue false

        let action (json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let! result = client.GetCollectionAsync<MatchSuggestionDto>("/api/v1/bills/match/suggestions")

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok suggestions ->
                    if json then
                        printJson suggestions
                    else
                        printMatchSuggestions suggestions
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "suggestions" {
            description "List transaction-bill match suggestions"
            inputs jsonOpt
            setAction action
        }

    /// `match execute`: POST /api/v1/bills/match
    let executeCommand : System.CommandLine.Command =
        let txIdOpt = option<int64> "--transaction-id" |> desc "Transaction ID to match" |> defaultValue 0L
        let billIdOpt = option<int64> "--bill-id" |> desc "Bill ID to match" |> defaultValue 0L
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON" |> defaultValue false

        let action (txId: int64, billId: int64, json: bool) =
            async {
                if txId <= 0L then
                    printfn "Error: --transaction-id is required and must be positive"
                    return ExitCodes.generalError
                elif billId <= 0L then
                    printfn "Error: --bill-id is required and must be positive"
                    return ExitCodes.generalError
                else
                    let request: ApiMatchRequest =
                        { TransactionId = txId
                          BillId = billId }

                    let baseUrl = getBaseUrl ()
                    let client = FoxyBalanceClient(baseUrl)
                    let! result = client.PostAsync<TransactionDto>("/api/v1/bills/match", request)

                    match result with
                    | Error e ->
                        printfn "Error: %s" e
                        return ExitCodes.generalError
                    | Ok transaction ->
                        printfn "Match executed successfully."
                        if json then
                            printJson transaction
                        else
                            printTransaction transaction
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "execute" {
            description "Match a transaction to a recurring bill"
            inputs (txIdOpt, billIdOpt, jsonOpt)
            setAction action
        }

    /// Build the `match` parent command.
    let buildCommand : System.CommandLine.Command =
        command "match" {
            description "Manage transaction matching"
            inputs context
            helpAction
            addCommands [ suggestionsCommand; executeCommand ]
        }

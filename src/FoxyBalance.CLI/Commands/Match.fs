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
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let! result = client.GetCollectionWithLinksAsync<MatchSuggestionDto>("/api/v1/bills/match/suggestions")

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok collection ->
                    if json then
                        printHalCollectionJson collection
                    else
                        printMatchSuggestionsWithLinks collection.Items
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "suggestions" {
            description "List transaction-bill match suggestions"
            inputs jsonOpt
            setAction action
        }

    /// `match execute`: POST /api/v1/bills/match
    /// Accepts either numeric IDs or HATEOAS link hrefs for --transaction-id and --bill-id.
    let executeCommand : System.CommandLine.Command =
        let txIdOpt = option<string> "--transaction-id" |> desc "Transaction ID or HATEOAS link href (must be positive)" |> defaultValue "0"
        let billIdOpt = option<string> "--bill-id" |> desc "Bill ID or HATEOAS link href (must be positive)" |> defaultValue "0"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (txIdOrLink: string, billIdOrLink: string, json: bool) =
            async {
                // Resolve IDs: accept either numeric IDs or HATEOAS link hrefs
                let txId =
                    match LinkResolver.tryParseId txIdOrLink with
                    | Some id -> id
                    | None -> LinkResolver.extractIdFromHref txIdOrLink |> Option.defaultValue 0L

                let billId =
                    match LinkResolver.tryParseId billIdOrLink with
                    | Some id -> id
                    | None -> LinkResolver.extractIdFromHref billIdOrLink |> Option.defaultValue 0L

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
                    let! result = client.PostResourceAsync<TransactionDto>("/api/v1/bills/match", request)

                    match result with
                    | Error e ->
                        printfn "Error: %s" e
                        return ExitCodes.generalError
                    | Ok resource ->
                        printfn "Match executed successfully."
                        if json then
                            printHalResourceJson resource
                        else
                            printTransactionWithLinks resource
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "execute" {
            description "Match a transaction to a recurring bill (accepts IDs or HATEOAS links)"
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

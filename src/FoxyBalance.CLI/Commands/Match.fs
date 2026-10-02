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
                let! result = client.GetCollectionWithLinksAsync(Codecs.matchSuggestionDtoDecoder, "/api/v1/bills/match/suggestions")

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok collection ->
                    if json then
                        printHalCollectionJson (Codecs.matchSuggestionDtoEncoder, collection)
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
        let recurringTransactionIdOpt = option<string> "--recurring-transaction-id" |> desc "Recurring Transaction ID or HATEOAS link href (must be positive)" |> defaultValue "0"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (txIdOrLink: string, recurringTransactionIdOrLink: string, json: bool) =
            async {
                // Resolve IDs: accept either numeric IDs or HATEOAS link hrefs
                let txId =
                    match LinkResolver.tryParseId txIdOrLink with
                    | Some id -> id
                    | None -> LinkResolver.extractIdFromHref txIdOrLink |> Option.defaultValue 0L

                let recurringTransactionId =
                    match LinkResolver.tryParseId recurringTransactionIdOrLink with
                    | Some id -> id
                    | None -> LinkResolver.extractIdFromHref recurringTransactionIdOrLink |> Option.defaultValue 0L

                if txId <= 0L then
                    printfn "Error: --transaction-id is required and must be positive"
                    return ExitCodes.generalError
                elif recurringTransactionId <= 0L then
                    printfn "Error: --recurring-transaction-id is required and must be positive"
                    return ExitCodes.generalError
                else
                    let request: ApiMatchRequest =
                        { TransactionId = txId
                          RecurringTransactionId = recurringTransactionId }

                    let baseUrl = getBaseUrl ()
                    let client = FoxyBalanceClient(baseUrl)
                    let! result = client.PostResourceAsync(Codecs.apiMatchRequestEncoder, Codecs.transactionDtoDecoder, "/api/v1/bills/match", request)

                    match result with
                    | Error e ->
                        printfn "Error: %s" e
                        return ExitCodes.generalError
                    | Ok resource ->
                        printfn "Match executed successfully."
                        if json then
                            printHalResourceJson (Codecs.transactionDtoEncoder, resource)
                        else
                            printTransactionWithLinks resource
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "execute" {
            description "Match a transaction to a recurring bill (accepts IDs or HATEOAS links)"
            inputs (txIdOpt, recurringTransactionIdOpt, jsonOpt)
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

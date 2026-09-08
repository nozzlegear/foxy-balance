namespace FoxyBalance.CLI.Commands

open System
open FSharp.SystemCommandLine
open FSharp.SystemCommandLine.Input
open FoxyBalance.CLI
open FoxyBalance.CLI.Domain
open FoxyBalance.CLI.TokenStore
open FoxyBalance.CLI.Formatters

module Balance =

    /// `balance view`: GET /api/v1/balance
    let viewCommand : System.CommandLine.Command =
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let! result = client.GetResourceAsync(Codecs.transactionSumDecoder, "/api/v1/balance")

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok resource ->
                    if json then
                        printHalResourceJson (Codecs.transactionSumEncoder, resource)
                    else
                        printBalanceWithLinks resource
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "view" {
            description "View balance summary"
            inputs jsonOpt
            setAction action
        }

    /// `balance at-date`: GET /api/v1/balance/as-of-date?date=yyyy-MM-dd&includePending=true|false
    let atDateCommand : System.CommandLine.Command =
        let dateOpt = optionMaybe<string> "--date" |> desc "Date (yyyy-MM-dd)"
        let includePendingOpt = option<bool> "--include-pending" |> desc "Include pending transactions (default: false)" |> defaultValue false
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (date: string option, includePending: bool, json: bool) =
            async {
                match date with
                | None ->
                    printfn "Error: --date is required (format: yyyy-MM-dd)"
                    return ExitCodes.generalError
                | Some d when String.IsNullOrWhiteSpace d ->
                    printfn "Error: --date is required (format: yyyy-MM-dd)"
                    return ExitCodes.generalError
                | Some d ->
                    let baseUrl = getBaseUrl ()
                    let client = FoxyBalanceClient(baseUrl)
                    let path = sprintf "/api/v1/balance/as-of-date?date=%s&includePending=%b" d includePending
                    let! result = client.GetResourceAsync(Codecs.transactionSumDecoder, path)

                    match result with
                    | Error e ->
                        printfn "Error: %s" e
                        return ExitCodes.generalError
                    | Ok resource ->
                        if json then
                            printHalResourceJson (Codecs.transactionSumEncoder, resource)
                        else
                            printBalanceWithLinks resource
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "at-date" {
            description "View balance as of a specific date"
            inputs (dateOpt, includePendingOpt, jsonOpt)
            setAction action
        }

    /// `balance before-transaction <id-or-link>`: GET /api/v1/balance/before-transaction/{id}?useClearDate=true|false
    let beforeTransactionCommand : System.CommandLine.Command =
        let idArg = argument<string> "id" |> desc "Transaction ID or HATEOAS link href"
        let useClearDateOpt = option<bool> "--use-clear-date" |> desc "Use clear date instead of creation date (default: false)" |> defaultValue false
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (idOrLink: string, useClearDate: bool, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                // Resolve the transaction link to extract the ID, then build the balance path
                let txPath = LinkResolver.resolvePath "/api/v1/transactions" idOrLink
                let txId = LinkResolver.extractIdFromHref txPath |> Option.defaultValue 0L
                if txId <= 0L then
                    printfn "Error: Could not extract transaction ID from '%s'" idOrLink
                    return ExitCodes.generalError
                else
                    let path = sprintf "/api/v1/balance/before-transaction/%d?useClearDate=%b" txId useClearDate
                    let! result = client.GetResourceAsync(Codecs.transactionSumDecoder, path)

                    match result with
                    | Error e ->
                        printfn "Error: %s" e
                        return ExitCodes.generalError
                    | Ok resource ->
                        if json then
                            printHalResourceJson (Codecs.transactionSumEncoder, resource)
                        else
                            printBalanceWithLinks resource
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "before-transaction" {
            description "View balance before a specific transaction (accepts ID or HATEOAS link)"
            inputs (idArg, useClearDateOpt, jsonOpt)
            setAction action
        }

    /// `balance after-transaction <id-or-link>`: GET /api/v1/balance/after-transaction/{id}?useClearDate=true|false
    let afterTransactionCommand : System.CommandLine.Command =
        let idArg = argument<string> "id" |> desc "Transaction ID or HATEOAS link href"
        let useClearDateOpt = option<bool> "--use-clear-date" |> desc "Use clear date instead of creation date (default: false)" |> defaultValue false
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (idOrLink: string, useClearDate: bool, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let txPath = LinkResolver.resolvePath "/api/v1/transactions" idOrLink
                let txId = LinkResolver.extractIdFromHref txPath |> Option.defaultValue 0L
                if txId <= 0L then
                    printfn "Error: Could not extract transaction ID from '%s'" idOrLink
                    return ExitCodes.generalError
                else
                    let path = sprintf "/api/v1/balance/after-transaction/%d?useClearDate=%b" txId useClearDate
                    let! result = client.GetResourceAsync(Codecs.transactionSumDecoder, path)

                    match result with
                    | Error e ->
                        printfn "Error: %s" e
                        return ExitCodes.generalError
                    | Ok resource ->
                        if json then
                            printHalResourceJson (Codecs.transactionSumEncoder, resource)
                        else
                            printBalanceWithLinks resource
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "after-transaction" {
            description "View balance after a specific transaction (accepts ID or HATEOAS link)"
            inputs (idArg, useClearDateOpt, jsonOpt)
            setAction action
        }

    /// Build the `balance` parent command.
    let buildCommand : System.CommandLine.Command =
        command "balance" {
            description "View balance information"
            inputs context
            helpAction
            addCommands [ viewCommand; atDateCommand; beforeTransactionCommand; afterTransactionCommand ]
        }

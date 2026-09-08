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

    /// Build the `balance` parent command.
    let buildCommand : System.CommandLine.Command =
        command "balance" {
            description "View balance information"
            inputs context
            helpAction
            addCommand viewCommand
        }

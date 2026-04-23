namespace FoxyBalance.CLI.Commands

open System
open FSharp.SystemCommandLine
open FSharp.SystemCommandLine.Input
open FoxyBalance.CLI
open FoxyBalance.CLI.Domain
open FoxyBalance.CLI.TokenStore
open FoxyBalance.CLI.TokenRefresh

module Auth =

    /// `auth login`: Exchange API key + secret for tokens, store in keychain.
    let loginCommand : System.CommandLine.Command =
        let apiKeyOpt = optionMaybe<string> "--api-key" |> desc "API key (if not provided, will prompt interactively)"
        let apiSecretOpt = optionMaybe<string> "--api-secret" |> desc "API secret (if not provided, will prompt interactively)"
        let baseUrlOpt = optionMaybe<string> "--base-url" |> desc "Base URL of the FoxyBalance API"

        let action (apiKey: string option, apiSecret: string option, baseUrl: string option) =
            async {
                let resolvedBaseUrl =
                    match baseUrl with
                    | Some url -> url
                    | None -> getBaseUrl ()

                let key =
                    match apiKey with
                    | Some k when not (String.IsNullOrWhiteSpace k) -> k
                    | _ ->
                        printf "Enter API key: "
                        Console.ReadLine()

                let secret =
                    match apiSecret with
                    | Some s when not (String.IsNullOrWhiteSpace s) -> s
                    | _ ->
                        printf "Enter API secret: "
                        Console.ReadLine()

                if String.IsNullOrWhiteSpace key || String.IsNullOrWhiteSpace secret then
                    printfn "Error: API key and secret are required."
                    return ExitCodes.generalError
                else
                    let client = FoxyBalanceClient(resolvedBaseUrl)
                    let! result = client.ExchangeTokens(key, secret)

                    match result with
                    | Error e ->
                        printfn "Login failed: %s" e
                        return ExitCodes.authError
                    | Ok tokenResponse ->
                        match saveTokens tokenResponse.AccessToken tokenResponse.RefreshToken resolvedBaseUrl with
                        | Ok () ->
                            printfn "Successfully logged in."
                            return ExitCodes.success
                        | Error e ->
                            printfn "Login succeeded but failed to save credentials: %s" e
                            return ExitCodes.generalError
            }
            |> Async.RunSynchronously

        command "login" {
            description "Authenticate and store API credentials in keychain"
            inputs (apiKeyOpt, apiSecretOpt, baseUrlOpt)
            setAction action
        }

    /// `auth logout`: Clear stored credentials.
    let logoutCommand : System.CommandLine.Command =
        let action (ctx: ActionContext) =
            match clearTokens () with
            | Ok () ->
                printfn "Logged out. Credentials removed from keychain."
                ExitCodes.success
            | Error e ->
                printfn "Error clearing credentials: %s" e
                ExitCodes.generalError

        command "logout" {
            description "Remove stored API credentials from keychain"
            inputs context
            setAction action
        }

    /// `auth status`: Show current auth status.
    let statusCommand : System.CommandLine.Command =
        let action (ctx: ActionContext) =
            match loadTokens () with
            | None ->
                printfn "Not authenticated."
                printfn "Run 'foxy-balance auth login' to authenticate."
                ExitCodes.generalError
            | Some config ->
                printfn "Authenticated."
                printfn "  Base URL: %s" config.BaseUrl
                let masked =
                    if config.AccessToken.Length > 14 then
                        config.AccessToken.Substring(0, 10) + "..." + config.AccessToken.Substring(config.AccessToken.Length - 4)
                    else
                        config.AccessToken
                printfn "  Access token: %s" masked
                printfn "  Refresh token: stored"
                ExitCodes.success

        command "status" {
            description "Show current authentication status"
            inputs context
            setAction action
        }

    /// `auth refresh`: Manually refresh tokens.
    let refreshCommand : System.CommandLine.Command =
        let action (ctx: ActionContext) =
            async {
                match loadTokens () with
                | None ->
                    printfn "Not authenticated. Run 'foxy-balance auth login' first."
                    return ExitCodes.authError
                | Some config ->
                    let! result = TokenRefresh.refresh config.RefreshToken config.BaseUrl
                    match result with
                    | Ok _ ->
                        printfn "Token refreshed successfully."
                        return ExitCodes.success
                    | Error e ->
                        printfn "Token refresh failed: %s" e
                        printfn "You may need to re-authenticate with 'foxy-balance auth login'."
                        return ExitCodes.authError
            }
            |> Async.RunSynchronously

        command "refresh" {
            description "Manually refresh the access token"
            inputs context
            setAction action
        }

    /// Build the `auth` parent command with all subcommands.
    let buildCommand : System.CommandLine.Command =
        command "auth" {
            description "Authenticate and manage API credentials"
            noAction
            addCommands [ loginCommand; logoutCommand; statusCommand; refreshCommand ]
        }

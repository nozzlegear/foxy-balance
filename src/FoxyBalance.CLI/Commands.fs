module FoxyBalance.CLI.CommandRoot

open System
open System.Reflection
open FSharp.SystemCommandLine
open FSharp.SystemCommandLine.Input
open FoxyBalance.CLI.Commands

/// Build the root command with all topic subcommands.
let entry argv =
    let version =
        Assembly
            .GetExecutingAssembly()
            .GetCustomAttributes<AssemblyInformationalVersionAttribute>()
        |> Seq.tryHead
        |> Option.map (fun a -> a.InformationalVersion)
        |> Option.defaultValue "1.0.0"

    rootCommand argv {
        description $"FoxyBalance CLI v{version} - Command-line interface for the FoxyBalance API"

        addCommands [
            Auth.buildCommand
            Balance.buildCommand
            Transactions.buildCommand
            Bills.buildCommand
            Match.buildCommand
        ]

        noAction
    }

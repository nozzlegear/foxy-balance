module FoxyBalance.CLI.CommandRoot

open System.Reflection
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

    let rootCmd = System.CommandLine.RootCommand($"FoxyBalance CLI v{version} - Command-line interface for the FoxyBalance API")

    rootCmd.Add(Auth.buildCommand)
    rootCmd.Add(Balance.buildCommand)
    rootCmd.Add(Transactions.buildCommand)
    rootCmd.Add(Bills.buildCommand)
    rootCmd.Add(Match.buildCommand)

    // Set help action for when no subcommand is provided
    rootCmd.SetAction(fun pr ->
        System.CommandLine.Help.HelpAction().Invoke(pr)
    )

    rootCmd
        .Parse(argv |> List.ofArray)
        .Invoke()

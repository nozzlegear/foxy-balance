namespace FoxyBalance.Migrations

open System.ComponentModel.DataAnnotations
open System.Threading.Tasks
open FoxyBalance.Migrations
open Microsoft.Extensions.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options

[<CLIMutable>]
type ConnectionStrings = {
    [<Required>]
    SqlDatabase: string
}

type HostedMigrationService(
    connectionStringOptions: IOptions<ConnectionStrings>,
    loggerFactory: ILoggerFactory
) =
    interface IHostedLifecycleService with
        member _.StartingAsync _ =
            // Migrate the SQL database to the latest version
            Migrator.migrate(Migrator.Latest, connectionStringOptions.Value.SqlDatabase, loggerFactory)
            Task.CompletedTask
        member this.StartAsync _ =
            Task.CompletedTask
        member this.StartedAsync _ =
            Task.CompletedTask
        member this.StopAsync _ =
            Task.CompletedTask
        member this.StoppedAsync _ =
            Task.CompletedTask
        member this.StoppingAsync _ =
            Task.CompletedTask

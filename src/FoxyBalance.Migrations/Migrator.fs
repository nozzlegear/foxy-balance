namespace FoxyBalance.Migrations

open System
open FluentMigrator.Runner
open Microsoft.Extensions.DependencyInjection

module Migrator =
    type MigrationTarget =
        | Latest
        | Up of int64
        | Down of int64

    /// Migrates the PostgreSQL database to the desired target migration.
    let migrate (target: MigrationTarget) (connStr : string) =
        let serviceProvider =
            ServiceCollection()
                .AddFluentMigratorCore()
                .ConfigureRunner(fun rb ->
                    rb
                        .AddPostgres()
                        .WithGlobalConnectionString(connStr)
                        .ScanIn(typeof<Migration_001_InitialPostgreSQLSchema>.Assembly).For.Migrations()
                    |> ignore)
                .AddLogging(fun lb -> lb.AddFluentMigratorConsole() |> ignore)
                .BuildServiceProvider(false)

        use scope = serviceProvider.CreateScope()
        let runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>()

        match target with
        | Latest -> runner.MigrateUp()
        | Up version -> runner.MigrateUp(version)
        | Down version -> runner.MigrateDown(version)

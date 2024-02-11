namespace FoxyBalance.Migrations

open Microsoft.Extensions.Logging
open SimpleMigrations
open SimpleMigrations.DatabaseProvider
open System.Data.SqlClient

module Migrator =
    type MigrationTarget =
        | Latest
        | Baseline of int64
        | Target of int64
    
    /// Migrates the SQL database to the desired target migration.
    let migrate (direction, connStr : string, loggerFactory: ILoggerFactory) =
        let logger =
            loggerFactory.CreateLogger<SimpleMigrator>()
            |> CustomLogger
        let assembly = typeof<Migration_01>.Assembly

        use connection = new SqlConnection(connStr)
        connection.Open()
        let provider = MssqlDatabaseProvider connection
        // Customize the name of the migration history table
        provider.TableName <- "FoxyBalance_Migrations"
        let migrator = SimpleMigrator(assembly, provider, logger)
        
        migrator.Load()
        
        match direction with
        | Latest ->
            migrator.MigrateToLatest()
        | Baseline target ->
            migrator.Baseline target
        | Target target ->
            migrator.MigrateTo target

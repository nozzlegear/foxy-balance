namespace FoxyBalance.Migrations

open System.Data.Common
open SimpleMigrations
open SimpleMigrations.DatabaseProvider
open System.Data.SqlClient

module Migrator =
    type MigrationTarget =
        | Latest
        | Baseline of int64
        | Target of int64
    
    /// Migrates the SQL database to the desired target migration.
    let migrate direction (connStr : string) =
        let assembly = typeof<FoxyBalance.Migrations.Migration_01>.Assembly
        
        use connection = new SqlConnection(connStr)
        connection.Open()
        let provider = MssqlDatabaseProvider connection
        let migrator = SimpleMigrator(assembly, provider)
        
        migrator.Load()
        
        match direction with
        | Latest ->
            migrator.MigrateToLatest()
        | Baseline target ->
            migrator.Baseline target
        | Target target ->
            migrator.MigrateTo target

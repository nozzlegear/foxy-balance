namespace FoxyBalance.Database.Tests

open System
open System.Threading.Tasks
open Npgsql
open Testcontainers.PostgreSql
open FoxyBalance.Migrations
open Xunit

type TargetMigration = Action

type ContainerMigrationStrategy =
    | MigrateOnStartup
    | DoNotMigrate

module private PgAdmin =
    let exec (cs: string) (sql: string) =
        task {
            use cn = new NpgsqlConnection(cs)
            do! cn.OpenAsync()
            use cmd = new NpgsqlCommand(sql, cn)
            let! _ = cmd.ExecuteNonQueryAsync()
            return ()
        }

    let recreateDb (cs: string) (db: string) =
        task {
            // Terminate existing connections to the database
            let terminateConnections = $"""
SELECT pg_terminate_backend(pg_stat_activity.pid)
FROM pg_stat_activity
WHERE pg_stat_activity.datname = '{db}'
  AND pid <> pg_backend_pid();
"""
            do! exec cs terminateConnections

            // Drop and recreate the database
            do! exec cs $"DROP DATABASE IF EXISTS {db};"
            do! exec cs $"CREATE DATABASE {db};"
        }

    let dropDbIfExists (cs: string) (db: string) =
        task {
            // Terminate existing connections to the database
            let terminateConnections = $"""
SELECT pg_terminate_backend(pg_stat_activity.pid)
FROM pg_stat_activity
WHERE pg_stat_activity.datname = '{db}'
  AND pid <> pg_backend_pid();
"""
            do! exec cs terminateConnections

            // Drop the database if it exists
            do! exec cs $"DROP DATABASE IF EXISTS {db};"
        }

type DbContainerFixture() =
    static let [<Literal>] image = "docker.io/library/postgres:18-alpine"
    static let [<Literal>] digest = "sha256:154ea39af68ff30dec041cd1f1b5600009993724c811dbadde54126eb10bedd1"

    let mutable container: PostgreSqlContainer = null
    let mutable dbName = String.Empty
    let mutable dbConn = String.Empty

    abstract member MigrationStrategy: ContainerMigrationStrategy
    default this.MigrationStrategy = ContainerMigrationStrategy.MigrateOnStartup

    member _.DatabaseName = dbName
    member _.ConnectionString = dbConn

    interface IAsyncLifetime with
        member this.InitializeAsync(): ValueTask =
            ValueTask(task {
                let builder = PostgreSqlBuilder()
                container <- builder
                    .WithImage($"{image}@{digest}")
                    .Build()

                do! container.StartAsync(TestContext.Current.CancellationToken)

                let runId =
                    Environment.GetEnvironmentVariable("CI_RUN_ID")
                    |> function
                       | null | "" -> Guid.NewGuid().ToString("N")[..7]
                       | v -> v

                dbName <- $"foxybalance_test_{runId}_{DateTimeOffset.UtcNow.Ticks}"
                let connStr = container.GetConnectionString()

                do! PgAdmin.recreateDb connStr dbName
                dbConn <- connStr + $";Database={dbName}"

                if this.MigrationStrategy = ContainerMigrationStrategy.MigrateOnStartup then
                    Migrator.migrate Migrator.MigrationTarget.Latest dbConn
            })

        member this.DisposeAsync(): ValueTask =
            ValueTask(task {
                if not (String.IsNullOrWhiteSpace dbName) && container <> null then
                    do! PgAdmin.dropDbIfExists (container.GetConnectionString()) dbName

                if container <> null then
                    do! container.DisposeAsync()
            })

[<Sealed>]
type UnmigratedSqlContainerFixture() =
    inherit DbContainerFixture()
    override this.MigrationStrategy = ContainerMigrationStrategy.DoNotMigrate

[<CollectionDefinition("UserDatabase")>]
type UserDatabaseCollection() =
    interface ICollectionFixture<DbContainerFixture>

[<CollectionDefinition("TransactionDatabase")>]
type TransactionDatabaseCollection() =
    interface ICollectionFixture<DbContainerFixture>

[<CollectionDefinition("IncomeDatabase")>]
type IncomeDatabaseCollection() =
    interface ICollectionFixture<DbContainerFixture>

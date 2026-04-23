namespace FoxyBalance.Database.Tests

open System
open System.Threading.Tasks
open Npgsql
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
            // Connect to the default postgres database to drop the target database
            // We need to strip Database= from the connection string and use postgres instead
            let postgresCs =
                cs.Split(';', StringSplitOptions.RemoveEmptyEntries)
                |> Array.filter (fun s -> not (s.StartsWith("Database=", StringComparison.OrdinalIgnoreCase)))
                |> String.concat ";"
                |> fun baseCs -> if String.IsNullOrWhiteSpace baseCs then baseCs else baseCs + ";Database=postgres"

            // Terminate existing connections to the database
            let terminateConnections = $"""
SELECT pg_terminate_backend(pg_stat_activity.pid)
FROM pg_stat_activity
WHERE pg_stat_activity.datname = '{db}'
  AND pid <> pg_backend_pid();
"""
            do! exec postgresCs terminateConnections

            // Drop the database if it exists
            do! exec postgresCs $"DROP DATABASE IF EXISTS {db};"
        }

type DbContainerFixture() =
    static let [<Literal>] image = "docker.io/library/postgres:18-alpine"
    static let [<Literal>] digest = "sha256:154ea39af68ff30dec041cd1f1b5600009993724c811dbadde54126eb10bedd1"

    let mutable dbName = String.Empty
    let mutable dbConn = String.Empty

    abstract member MigrationStrategy: ContainerMigrationStrategy
    default this.MigrationStrategy = ContainerMigrationStrategy.MigrateOnStartup

    member _.DatabaseName = dbName
    member _.ConnectionString = dbConn
    interface IAsyncLifetime with
        member this.InitializeAsync(): ValueTask =
            ValueTask(task {
                let args = "-e POSTGRES_PASSWORD=postgres"
                let buildConnStr ip = $"Host={ip};Port=5433;Username=postgres;Password=postgres"
                let runMigrations = this.MigrationStrategy = ContainerMigrationStrategy.MigrateOnStartup
                let migrateFn cs = Migrator.migrate Migrator.MigrationTarget.Latest cs
                let testcontainersCallback _ = ()

                let! result =
                    ContainerReuse.startContainerAndReuse
                        "postgres" image digest args buildConnStr
                        PgAdmin.recreateDb runMigrations migrateFn
                        testcontainersCallback
                        TestContext.Current.CancellationToken

                dbName <-
                    result.ConnectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
                    |> Array.toList
                    |> List.tryFind (fun s -> s.StartsWith("Database="))
                    |> Option.map (fun s -> s.Substring(9))
                    |> function
                       | Some db -> db
                       | None -> String.Empty
                dbConn <- result.ConnectionString
            })

        member this.DisposeAsync(): ValueTask =
            ValueTask(task {
                if not (String.IsNullOrWhiteSpace dbName) && not (String.IsNullOrWhiteSpace dbConn) then
                    do! PgAdmin.dropDbIfExists dbConn dbName
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

[<CollectionDefinition("RecurringBillDatabase")>]
type RecurringBillDatabaseCollection() =
    interface ICollectionFixture<DbContainerFixture>

[<CollectionDefinition("BillMatchingService")>]
type BillMatchingServiceCollection() =
    interface ICollectionFixture<DbContainerFixture>

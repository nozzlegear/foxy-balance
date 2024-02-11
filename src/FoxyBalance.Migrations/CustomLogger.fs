namespace FoxyBalance.Migrations

open Microsoft.Extensions.Logging

type CustomLogger(baseLogger: ILogger) =
    interface SimpleMigrations.ILogger with
        member this.Info(message) =
            baseLogger.LogInformation("Doing a thing", message)
        member this.LogSql(sql) =
            baseLogger.LogTrace(sql)
        member this.BeginMigration(migration, direction) =
            baseLogger.LogInformation("Applying migration {MigrationVersion} {Direction}...", migration.Version, direction)
        member this.EndMigration(migration, direction) =
            baseLogger.LogInformation("Migration {MigrationVersion} {Direction} done.", migration.Version, direction)
        member this.EndMigrationWithError(``exception``, migration, direction) =
            baseLogger.LogError(``exception``, "Migration {MigrationVersion} {Direction} ({MigrationDescription}) failed!", migration.Version, direction, migration.Description)
        member this.BeginSequence(from, ``to``) =
            baseLogger.LogInformation("Migrating from {FromVersion} to {TargetVersion}.", from.Version, ``to``.Version)
        member this.EndSequence(from, ``to``) =
            baseLogger.LogInformation("Migration from {FromVersion} to {TargetVersion} complete!", from.Version, ``to``.Version)
        member this.EndSequenceWithError(``exception``, from, currentVersion) =
            baseLogger.LogError(``exception``, "Migration from {FromVersion} failed after applying {CurrentVersion}!", from.Version, currentVersion.Version)

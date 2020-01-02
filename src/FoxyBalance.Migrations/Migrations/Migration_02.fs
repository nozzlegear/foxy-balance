namespace FoxyBalance.Migrations

open SimpleMigrations

[<Migration(02L, "Rename CompletedDate column to DateCleared; Remove ExpectedChargeDate column.")>]
type Migration_02() =
    inherit Migration()
    override this.Down() =
        this.Execute
            "ALTER TABLE FoxyBalance_Transactions ADD [ExpectedChargeDate] datetime2 null"
        this.Execute
            "EXEC sp_rename 'FoxyBalance_Transactions.[DateCleared]', 'Date', 'COLUMN'";
        
    override this.Up() =
        this.Execute
            "EXEC sp_rename 'FoxyBalance_Transactions.[CompletedDate]', 'DateCleared', 'COLUMN'";
        this.Execute
            "ALTER TABLE FoxyBalance_Transactions DROP COLUMN [ExpectedChargeDate]"

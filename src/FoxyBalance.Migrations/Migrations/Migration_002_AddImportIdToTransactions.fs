namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(2L, "Add importid column to transactions table for tracking imported transactions")>]
type Migration_002_AddImportIdToTransactions() =
    inherit Migration()

    override this.Up() =
        this.Execute.Sql("""
            ALTER TABLE foxybalance_transactions
            ADD COLUMN importid VARCHAR(255);
        """)

    override this.Down() =
        this.Execute.Sql("""
            ALTER TABLE foxybalance_transactions DROP COLUMN importid;
        """)

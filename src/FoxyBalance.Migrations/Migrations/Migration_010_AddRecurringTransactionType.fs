namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(10L, "Add type column to foxybalance_recurringtransactions for bill/income distinction")>]
type Migration_010_AddRecurringTransactionType() =
    inherit Migration()

    override this.Up() =
        this.Execute.Sql("""
            ALTER TABLE foxybalance_recurringtransactions
            ADD COLUMN recurringtype VARCHAR(20) NOT NULL DEFAULT 'bill' CONSTRAINT chk_recurring_type CHECK (recurringtype IN ('bill', 'income'));
        """)

    override this.Down() =
        this.Execute.Sql("""
            ALTER TABLE foxybalance_recurringtransactions DROP COLUMN recurringtype;
        """)

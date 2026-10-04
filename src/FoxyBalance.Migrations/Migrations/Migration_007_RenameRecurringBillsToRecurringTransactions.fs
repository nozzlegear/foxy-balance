namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(7L, "Rename recurringbills → recurringtransactions (table and column) to match codebase")>]
type Migration_007_RenameRecurringBillsToRecurringTransactions() =
    inherit Migration()

    override this.Up() =
        // Production rename: checks if old table exists before renaming (skip on fresh installs).
        this.Execute.Sql("""
            DO $$
                DECLARE hasOldTable BOOLEAN;
            BEGIN
                SELECT EXISTS (SELECT 1 FROM information_schema.tables
                    WHERE table_name = 'foxybalance_recurringbills') INTO hasOldTable;

                IF hasOldTable THEN
                    ALTER TABLE foxybalance_recurringbills RENAME TO foxybalance_recurringtransactions;
                    ALTER TABLE foxybalance_transactions RENAME COLUMN recurringbillid TO recurringtransactionid;

                    DROP INDEX IF EXISTS idx_transactions_recurringbillid;
                    CREATE INDEX idx_transactions_recurringtransactionid
                        ON foxybalance_transactions (recurringtransactionid)
                        WHERE recurringtransactionid IS NOT NULL;
                END IF;
            END $$;
        """)

    override this.Down() =
        // Undo: recreate old names and drop new ones
        this.Execute.Sql("""
            ALTER TABLE foxybalance_recurringtransactions RENAME COLUMN recurringtransactionid TO recurringbillid;
        """)
        this.Execute.Sql("DROP INDEX IF EXISTS idx_transactions_recurringtransactionid;")
        this.Execute.Sql("""
            CREATE INDEX idx_transactions_recurringbillid
            ON foxybalance_transactions (recurringbillid)
            WHERE recurringbillid IS NOT NULL;
        """)
        this.Execute.Sql("ALTER TABLE foxybalance_recurringtransactions RENAME TO foxybalance_recurringbills;")

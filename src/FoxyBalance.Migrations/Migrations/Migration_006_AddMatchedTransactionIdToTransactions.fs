namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(6L, "Add matched transaction id to transactions table")>]
type Migration_006_AddMatchedTransactionIdToTransactions() =
    inherit Migration()

    override this.Up() =
        this.Execute.Sql("""
            ALTER TABLE foxybalance_transactions
            ADD COLUMN matchedtransactionid BIGINT REFERENCES foxybalance_transactions(id) ON DELETE SET NULL;

            CREATE INDEX idx_transactions_matchedtransactionid
            ON foxybalance_transactions (matchedtransactionid)
            WHERE matchedtransactionid IS NOT NULL;
        """)

    override this.Down() =
        this.Execute.Sql("DROP INDEX IF EXISTS idx_transactions_matchedtransactionid;")
        this.Execute.Sql("ALTER TABLE foxybalance_transactions DROP COLUMN IF EXISTS matchedtransactionid;")

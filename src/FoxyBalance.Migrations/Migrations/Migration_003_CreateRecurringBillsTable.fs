namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(3L, "Create recurring transactions table for managing monthly recurring charges")>]
type Migration_003_CreateRecurringBillsTable() =
    inherit Migration()

    override this.Up() =
        this.Execute.Sql("""
            CREATE TABLE foxybalance_recurringtransactions (
                id BIGSERIAL PRIMARY KEY,
                userid INT NOT NULL REFERENCES foxybalance_users(id),
                name VARCHAR(500) NOT NULL,
                amount NUMERIC(18,2) NOT NULL,
                scheduletype INT NOT NULL,
                scheduleweekofmonth INT,
                scheduledayofweek INT,
                scheduleddate INT,
                datecreated TIMESTAMPTZ NOT NULL,
                lastapplieddate TIMESTAMPTZ,
                active BOOLEAN NOT NULL DEFAULT true
            );
            CREATE INDEX idx_recurringtransactions_userid ON foxybalance_recurringtransactions (userid);
            CREATE INDEX idx_recurringtransactions_active ON foxybalance_recurringtransactions (active) WHERE active = true;
        """)

    override this.Down() =
        this.Delete.Table("foxybalance_recurringtransactions") |> ignore

namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(3L, "Create recurring bills table for managing monthly recurring charges")>]
type Migration_003_CreateRecurringBillsTable() =
    inherit Migration()

    override this.Up() =
        this.Execute.Sql("""
            CREATE TABLE foxybalance_recurringbills (
                id BIGSERIAL PRIMARY KEY,
                userid INT NOT NULL REFERENCES foxybalance_users(id),
                name VARCHAR(500) NOT NULL,
                amount NUMERIC(18,2) NOT NULL,
                weekofmonth INT NOT NULL,
                dayofweek INT NOT NULL,
                datecreated TIMESTAMPTZ NOT NULL,
                lastapplieddate TIMESTAMPTZ,
                active BOOLEAN NOT NULL DEFAULT true
            );
            CREATE INDEX idx_recurringbills_userid ON foxybalance_recurringbills (userid);
            CREATE INDEX idx_recurringbills_active ON foxybalance_recurringbills (active) WHERE active = true;
        """)

    override this.Down() =
        this.Delete.Table("foxybalance_recurringbills") |> ignore

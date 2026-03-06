namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(6L, "Add date-based scheduling support for recurring bills")>]
type Migration_006_AddDateBasedScheduling() =
    inherit Migration()

    override this.Up() =
        this.Execute.Sql("""
            ALTER TABLE foxybalance_recurringbills
                ADD COLUMN scheduletype VARCHAR(20) NOT NULL DEFAULT 'week_based',
                ADD COLUMN dayofmonth INT NULL;

            ALTER TABLE foxybalance_recurringbills
                ADD CONSTRAINT chk_schedule_pattern CHECK (
                    (scheduletype = 'week_based' AND dayofmonth IS NULL) OR
                    (scheduletype = 'date_based' AND dayofmonth BETWEEN 1 AND 31)
                );

            CREATE INDEX idx_recurringbills_scheduletype ON foxybalance_recurringbills(scheduletype);
        """)

    override this.Down() =
        this.Execute.Sql("""
            DROP INDEX IF EXISTS idx_recurringbills_scheduletype;
            ALTER TABLE foxybalance_recurringbills DROP CONSTRAINT IF EXISTS chk_schedule_pattern;
            ALTER TABLE foxybalance_recurringbills DROP COLUMN IF EXISTS dayofmonth;
            ALTER TABLE foxybalance_recurringbills DROP COLUMN IF EXISTS scheduletype;
        """)

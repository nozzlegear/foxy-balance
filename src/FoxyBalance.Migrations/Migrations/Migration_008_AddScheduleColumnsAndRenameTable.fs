namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(8L, "Add missing schedule columns and rename table to match current codebase schema")>]
type Migration_008_AddScheduleColumnsAndRenameTable() =
    inherit Migration()

    override this.Up() =
        // Align table to current codebase schema: rename old columns, add missing ones.
        this.Execute.Sql("""
            DO $$
                DECLARE hasOldTable BOOLEAN;

                BEGIN
                    -- Check if old table still exists (Migration_007 didn't run)
                    SELECT EXISTS (SELECT 1 FROM information_schema.tables
                        WHERE table_name = 'foxybalance_recurringbills') INTO hasOldTable;

                    IF hasOldTable THEN
                        ALTER TABLE foxybalance_recurringbills RENAME TO foxybalance_recurringtransactions;
                    END IF;

                    -- Rename old unprefixed columns to current codebase names (no-ops if already renamed)
                    ALTER TABLE foxybalance_recurringtransactions RENAME COLUMN IF EXISTS weekofmonth TO scheduleweekofmonth;
                    ALTER TABLE foxybalance_recurringtransactions RENAME COLUMN IF EXISTS dayofweek TO scheduledayofweek;

                    -- Add columns that don't exist in the old schema
                    ALTER TABLE foxybalance_recurringtransactions ADD COLUMN IF NOT EXISTS scheduletype INT DEFAULT 0;
                    ALTER TABLE foxybalance_recurringtransactions ADD COLUMN IF NOT EXISTS scheduleddate INT;

                END;
            $$;
        """)

    override this.Down() =
        // Reverse column changes from Up. Table rename reversed by Migration_007.Down().
        [this.Execute.Sql("ALTER TABLE foxybalance_recurringtransactions DROP COLUMN IF EXISTS scheduletype;"),
         this.Execute.Sql("ALTER TABLE foxybalance_recurringtransactions DROP COLUMN IF EXISTS scheduleddate;"),
         this.Execute.Sql("ALTER TABLE foxybalance_recurringtransactions RENAME COLUMN IF EXISTS scheduleweekofmonth TO weekofmonth;"),
         this.Execute.Sql("ALTER TABLE foxybalance_recurringtransactions RENAME COLUMN IF EXISTS scheduledayofweek TO dayofweek;")]
        |> ignore

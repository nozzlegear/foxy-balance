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
                END;
            $$;

            DO $$
                DECLARE tableExists BOOLEAN;
                DECLARE hasScheduleType BOOLEAN;
                DECLARE hasScheduledDate BOOLEAN;
                DECLARE hasWeekOfMonthColumn BOOLEAN;
                DECLARE hasDayOfWeekColumn BOOLEAN;

                BEGIN
                    -- Check if the transactions table exists
                    SELECT EXISTS (SELECT 1 FROM information_schema.tables
                        WHERE table_name = 'foxybalance_recurringtransactions') INTO tableExists;

                    IF NOT tableExists THEN
                        RETURN;
                    END IF;

                    -- Check existing columns
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduletype') INTO hasScheduleType;
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduleddate') INTO hasScheduledDate;
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduleweekofmonth') INTO hasWeekOfMonthColumn;
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduledayofweek') INTO hasDayOfWeekColumn;

                    IF NOT hasScheduleType THEN
                        ALTER TABLE foxybalance_recurringtransactions ADD COLUMN scheduletype INT DEFAULT 0;
                    END IF;

                    IF NOT hasScheduledDate THEN
                        ALTER TABLE foxybalance_recurringtransactions ADD COLUMN scheduledate INT;
                    END IF;

                    IF hasWeekOfMonthColumn AND NOT hasDayOfWeekColumn THEN
                        ALTER TABLE foxybalance_recurringtransactions RENAME COLUMN weekofmonth TO scheduleweekofmonth;
                        ALTER TABLE foxybalance_recurringtransactions RENAME COLUMN dayofweek TO scheduledayofweek;
                    END IF;
                END;
            $$;
        """)

    override this.Down() =
        // Reverse column changes from Up. Table rename reversed by Migration_007.Down().
        this.Execute.Sql("""
            DO $$
                DECLARE hasScheduleType BOOLEAN;
                DECLARE hasScheduledDate BOOLEAN;
                DECLARE hasWeekOfMonthColumn BOOLEAN;
                DECLARE hasDayOfWeekColumn BOOLEAN;

                BEGIN
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduletype') INTO hasScheduleType;
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduleddate') INTO hasScheduledDate;
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduleweekofmonth') INTO hasWeekOfMonthColumn;
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduledayofweek') INTO hasDayOfWeekColumn;

                    IF hasScheduleType THEN
                        ALTER TABLE foxybalance_recurringtransactions DROP COLUMN scheduletype;
                    END IF;

                    IF hasScheduledDate THEN
                        ALTER TABLE foxybalance_recurringtransactions DROP COLUMN scheduledate;
                    END IF;

                    IF hasWeekOfMonthColumn AND NOT hasDayOfWeekColumn THEN
                        ALTER TABLE foxybalance_recurringtransactions RENAME COLUMN scheduleweekofmonth TO weekofmonth;
                        ALTER TABLE foxybalance_recurringtransactions RENAME COLUMN scheduledayofweek TO dayofweek;
                    END IF;
                END;
            $$;
        """)

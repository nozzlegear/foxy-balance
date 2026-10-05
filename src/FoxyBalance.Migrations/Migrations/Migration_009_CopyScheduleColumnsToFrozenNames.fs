namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(9L, "Copy scheduleweekofmonth/scheduledayofweek columns and drop old weekofmonth/dayofweek columns")>]
type Migration_009_CopyScheduleColumnsToFrozenNames() =
    inherit Migration()

    override this.Up() =
        // Copy unprefixed weekofmonth → scheduleweekofmonth and dayofweek → scheduledayofweek,
        // then drop the old unprefixed columns (if they still exist).
        this.Execute.Sql("""
            DO $$
                DECLARE hasWeekOfMonthCol BOOLEAN;
                DECLARE hasDayOfWeekCol BOOLEAN;
                DECLARE hasScheduleWeekOfMonthCol BOOLEAN;
                DECLARE hasScheduleDayOfWeekCol BOOLEAN;

                BEGIN
                    -- Check which columns currently exist
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'weekofmonth') INTO hasWeekOfMonthCol;
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'dayofweek') INTO hasDayOfWeekCol;
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduleweekofmonth') INTO hasScheduleWeekOfMonthCol;
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduledayofweek') INTO hasScheduleDayOfWeekCol;

                    -- Add new columns if missing (no-op if they already exist from a prior run)
                    IF NOT hasScheduleWeekOfMonthCol THEN
                        ALTER TABLE foxybalance_recurringtransactions ADD COLUMN scheduleweekofmonth INT;
                    END IF;

                    IF NOT hasScheduleDayOfWeekCol THEN
                        ALTER TABLE foxybalance_recurringtransactions ADD COLUMN scheduledayofweek INT;
                    END IF;

                    -- Copy values from old unprefixed columns into new prefixed ones (where old columns still exist)
                    IF hasWeekOfMonthCol AND NOT hasScheduleWeekOfMonthCol THEN
                        UPDATE foxybalance_recurringtransactions SET scheduleweekofmonth = weekofmonth;
                    END IF;

                    IF hasDayOfWeekCol AND NOT hasScheduleDayOfWeekCol THEN
                        UPDATE foxybalance_recurringtransactions SET scheduledayofweek = dayofweek;
                    END IF;

                    -- Drop old unprefixed columns if they still exist (avoid error on re-run)
                    IF hasWeekOfMonthCol AND NOT hasScheduleWeekOfMonthCol THEN
                        ALTER TABLE foxybalance_recurringtransactions DROP COLUMN weekofmonth;
                    END IF;

                    IF hasDayOfWeekCol AND NOT hasScheduleDayOfWeekCol THEN
                        ALTER TABLE foxybalance_recurringtransactions DROP COLUMN dayofweek;
                    END IF;
                END;
            $$;
        """)

    override this.Down() =
        // Reverse: copy new columns back to old names and drop them, recreating the unprefixed versions.
        this.Execute.Sql("""
            DO $$
                DECLARE hasScheduleWeekOfMonthCol BOOLEAN;
                DECLARE hasScheduleDayOfWeekCol BOOLEAN;

                BEGIN
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduleweekofmonth') INTO hasScheduleWeekOfMonthCol;
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduledayofweek') INTO hasScheduleDayOfWeekCol;

                    IF hasScheduleWeekOfMonthCol AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'weekofmonth') THEN
                        ALTER TABLE foxybalance_recurringtransactions ADD COLUMN weekofmonth INT;
                    END IF;

                    IF hasScheduleDayOfWeekCol AND NOT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'dayofweek') THEN
                        ALTER TABLE foxybalance_recurringtransactions ADD COLUMN dayofweek INT;
                    END IF;

                    IF hasScheduleWeekOfMonthCol THEN
                        UPDATE foxybalance_recurringtransactions SET weekofmonth = scheduleweekofmonth;
                    END IF;

                    IF hasScheduleDayOfWeekCol THEN
                        UPDATE foxybalance_recurringtransactions SET dayofweek = scheduledayofweek;
                    END IF;

                    -- Drop the new prefixed columns that Up added
                    IF hasScheduleWeekOfMonthCol THEN
                        ALTER TABLE foxybalance_recurringtransactions DROP COLUMN scheduleweekofmonth;
                    END IF;

                    IF hasScheduleDayOfWeekCol THEN
                        ALTER TABLE foxybalance_recurringtransactions DROP COLUMN scheduledayofweek;
                    END IF;
                END;
            $$;
        """)

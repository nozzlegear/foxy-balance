namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(12L, "Add scheduleapplydate column for calendar-date schedules that may not exist in every month")>]
type Migration_012_AddScheduleApplyDateColumn() =
    inherit Migration()

    override this.Up() =
        // Nullable TEXT: "early" (LastDayOfMonth) or "late" (NextMonth1st), NULL when the
        // chosen day always exists or the legacy "skip month" behavior is desired.
        this.Execute.Sql("""
            DO $$
                DECLARE hasColumn BOOLEAN;
                BEGIN
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions'
                          AND column_name = 'scheduleapplydate') INTO hasColumn;

                    IF NOT hasColumn THEN
                        ALTER TABLE foxybalance_recurringtransactions
                            ADD COLUMN scheduleapplydate TEXT;
                    END IF;
                END;
            $$;
        """)

    override this.Down() =
        this.Execute.Sql("""
            DO $$
                DECLARE hasColumn BOOLEAN;
                BEGIN
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions'
                          AND column_name = 'scheduleapplydate') INTO hasColumn;

                    IF hasColumn THEN
                        ALTER TABLE foxybalance_recurringtransactions
                            DROP COLUMN scheduleapplydate;
                    END IF;
                END;
            $$;
        """)

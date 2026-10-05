namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(11L, "Rename scheduledate column to scheduleddate to match application code")>]
type Migration_011_RenameScheduledDateColumn() =
    inherit Migration()

    override this.Up() =
        // Migration_008 accidentally created `scheduledate` (single 'd') but the application
        // reads/writes `scheduleddate` (double 'd'). Rename the column if it exists under the
        // misspelled name and the correctly-spelled name is absent.
        this.Execute.Sql("""
            DO $$
                DECLARE hasMisspelled BOOLEAN;
                DECLARE hasCorrect BOOLEAN;

                BEGIN
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduledate') INTO hasMisspelled;
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduleddate') INTO hasCorrect;

                    IF hasMisspelled AND NOT hasCorrect THEN
                        ALTER TABLE foxybalance_recurringtransactions RENAME COLUMN scheduledate TO scheduleddate;
                    END IF;
                END;
            $$;
        """)

    override this.Down() =
        this.Execute.Sql("""
            DO $$
                DECLARE hasCorrect BOOLEAN;
                DECLARE hasMisspelled BOOLEAN;

                BEGIN
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduleddate') INTO hasCorrect;
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions' AND column_name = 'scheduledate') INTO hasMisspelled;

                    IF hasCorrect AND NOT hasMisspelled THEN
                        ALTER TABLE foxybalance_recurringtransactions RENAME COLUMN scheduleddate TO scheduledate;
                    END IF;
                END;
            $$;
        """)

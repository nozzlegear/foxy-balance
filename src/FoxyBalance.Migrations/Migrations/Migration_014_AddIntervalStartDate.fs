namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(14L, "Add byintervalstartdate column, backfill next occurrence for ByInterval schedules, add CHECK constraint")>]
type Migration_014_AddIntervalStartDate() =
    inherit Migration()

    override this.Up() =
        // byintervalstartdate anchors ByInterval occurrence enumeration. It is required for
        // scheduletype = 2 (enforced by the CHECK constraint) and NULL for other schedule types.
        //
        // Backfill rule: the next occurrence under the pre-migration due semantics, i.e.
        // lastapplieddate + interval (COALESCE defaults mirror the app reader: count=1, unit=day),
        // or now() for never-applied rows (they were due immediately). A past result simply means
        // overdue. Postgres interval month arithmetic clamps like .NET AddMonths (Jan 31 + 1 month
        // = Feb 28), so this matches the application's due-date computation exactly.
        this.Execute.Sql("""
            DO $$
                DECLARE hasColumn BOOLEAN;
                DECLARE hasConstraint BOOLEAN;
                BEGIN
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions'
                          AND column_name = 'byintervalstartdate') INTO hasColumn;

                    IF NOT hasColumn THEN
                        ALTER TABLE foxybalance_recurringtransactions
                            ADD COLUMN byintervalstartdate TIMESTAMPTZ;
                    END IF;

                    UPDATE foxybalance_recurringtransactions
                    SET byintervalstartdate =
                        CASE
                            WHEN lastapplieddate IS NOT NULL THEN
                                lastapplieddate + (COALESCE(byintervalcount, 1) || ' ' || COALESCE(byintervalunit, 'day') || 's')::interval
                            ELSE now()
                        END
                    WHERE scheduletype = 2 AND byintervalstartdate IS NULL;

                    SELECT EXISTS (SELECT 1 FROM pg_constraint
                        WHERE conname = 'chk_recurringtransactions_interval_startdate'
                          AND conrelid = 'foxybalance_recurringtransactions'::regclass) INTO hasConstraint;

                    IF NOT hasConstraint THEN
                        ALTER TABLE foxybalance_recurringtransactions
                            ADD CONSTRAINT chk_recurringtransactions_interval_startdate
                            CHECK (scheduletype <> 2 OR byintervalstartdate IS NOT NULL);
                    END IF;
                END;
            $$;
        """)

    override this.Down() =
        this.Execute.Sql("""
            DO $$
                DECLARE hasColumn BOOLEAN;
                DECLARE hasConstraint BOOLEAN;
                BEGIN
                    SELECT EXISTS (SELECT 1 FROM pg_constraint
                        WHERE conname = 'chk_recurringtransactions_interval_startdate'
                          AND conrelid = 'foxybalance_recurringtransactions'::regclass) INTO hasConstraint;

                    IF hasConstraint THEN
                        ALTER TABLE foxybalance_recurringtransactions
                            DROP CONSTRAINT chk_recurringtransactions_interval_startdate;
                    END IF;

                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions'
                          AND column_name = 'byintervalstartdate') INTO hasColumn;

                    IF hasColumn THEN
                        ALTER TABLE foxybalance_recurringtransactions
                            DROP COLUMN byintervalstartdate;
                    END IF;
                END;
            $$;
        """)

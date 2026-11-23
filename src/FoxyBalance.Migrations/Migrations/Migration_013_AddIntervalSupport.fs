namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(13L, "Add interval support columns for ByInterval schedule type")>]
type Migration_013_AddIntervalSupport() =
    inherit Migration()

    override this.Up() =
        // Add byintervalcount (INT) and byintervalunit (VARCHAR) columns for ByInterval schedules.
        // byintervalunit stores: "day", "week", or "month"
        this.Execute.Sql("""
            DO $$
                DECLARE hasCountColumn BOOLEAN;
                DECLARE hasUnitColumn BOOLEAN;
                BEGIN
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions'
                          AND column_name = 'byintervalcount') INTO hasCountColumn;

                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions'
                          AND column_name = 'byintervalunit') INTO hasUnitColumn;

                    IF NOT hasCountColumn THEN
                        ALTER TABLE foxybalance_recurringtransactions
                            ADD COLUMN byintervalcount INT;
                    END IF;

                    IF NOT hasUnitColumn THEN
                        ALTER TABLE foxybalance_recurringtransactions
                            ADD COLUMN byintervalunit VARCHAR(10);
                    END IF;
                END;
            $$;
        """)

    override this.Down() =
        this.Execute.Sql("""
            DO $$
                DECLARE hasCountColumn BOOLEAN;
                DECLARE hasUnitColumn BOOLEAN;
                BEGIN
                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions'
                          AND column_name = 'byintervalcount') INTO hasCountColumn;

                    SELECT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'foxybalance_recurringtransactions'
                          AND column_name = 'byintervalunit') INTO hasUnitColumn;

                    IF hasCountColumn THEN
                        ALTER TABLE foxybalance_recurringtransactions
                            DROP COLUMN byintervalcount;
                    END IF;

                    IF hasUnitColumn THEN
                        ALTER TABLE foxybalance_recurringtransactions
                            DROP COLUMN byintervalunit;
                    END IF;
                END;
            $$;
        """)

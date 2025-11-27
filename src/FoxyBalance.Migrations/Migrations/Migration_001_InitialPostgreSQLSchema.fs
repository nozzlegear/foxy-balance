namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(1L, "Initial PostgreSQL schema with auto-incrementing sequences")>]
type Migration_001_InitialPostgreSQLSchema() =
    inherit Migration()

    override this.Up() =
        // Create tables with SERIAL/BIGSERIAL for auto-incrementing IDs
        // SQL Server data can be imported with explicit IDs, then sequences will be reset

        // Create Users table
        this.Execute.Sql("""
            CREATE TABLE foxybalance_users (
                id SERIAL PRIMARY KEY,
                emailaddress VARCHAR(500) NOT NULL,
                datecreated TIMESTAMPTZ NOT NULL,
                hashedpassword TEXT NOT NULL
            );
            CREATE INDEX idx_emailaddress ON foxybalance_users (emailaddress);
        """)

        // Create TaxYears table
        this.Execute.Sql("""
            CREATE TABLE foxybalance_taxyears (
                id SERIAL PRIMARY KEY,
                userid INT NOT NULL REFERENCES foxybalance_users(id),
                taxyear INT NOT NULL,
                taxrate INT NOT NULL
            );
        """)

        // Create Transactions table
        this.Execute.Sql("""
            CREATE TABLE foxybalance_transactions (
                id BIGSERIAL PRIMARY KEY,
                userid INT NOT NULL REFERENCES foxybalance_users(id),
                name VARCHAR(500) NOT NULL,
                amount NUMERIC(18,2) NOT NULL,
                datecreated TIMESTAMPTZ NOT NULL,
                type VARCHAR(75) NOT NULL,
                recurring BOOLEAN NOT NULL,
                checknumber VARCHAR(25),
                status VARCHAR(75) NOT NULL,
                datecleared TIMESTAMPTZ
            );
            CREATE INDEX idx_userid ON foxybalance_transactions (userid);
        """)

        // Create IncomeRecords table
        this.Execute.Sql("""
            CREATE TABLE foxybalance_incomerecords (
                id BIGSERIAL PRIMARY KEY,
                userid INT NOT NULL REFERENCES foxybalance_users(id),
                taxyearid INT NOT NULL REFERENCES foxybalance_taxyears(id),
                saledate TIMESTAMPTZ NOT NULL,
                sourcetype VARCHAR(18) NOT NULL,
                sourcetransactionid VARCHAR(510),
                sourcetransactiondescription VARCHAR(1000),
                sourcetransactioncustomerdescription VARCHAR(510),
                saleamount INT NOT NULL,
                platformfee INT NOT NULL,
                processingfee INT NOT NULL,
                netshare INT NOT NULL,
                ignored BOOLEAN NOT NULL DEFAULT false
            );

            CREATE UNIQUE INDEX idx_incomerecords_source_user
            ON foxybalance_incomerecords (sourcetransactionid, userid)
            WHERE sourcetransactionid IS NOT NULL;

            CREATE INDEX idx_incomerecords_sourcetransactionid
            ON foxybalance_incomerecords (sourcetransactionid);
        """)

        // Create IncomeRecordsView
        this.Execute.Sql("""
            CREATE VIEW foxybalance_incomerecordsview AS
            SELECT
                ir.id,
                ir.userid,
                ir.taxyearid,
                ty.taxyear,
                ir.saledate,
                ir.sourcetype,
                ir.sourcetransactionid,
                ir.sourcetransactiondescription,
                ir.sourcetransactioncustomerdescription,
                ir.saleamount,
                ir.platformfee,
                ir.processingfee,
                ir.netshare,
                (ir.netshare * ty.taxrate::NUMERIC / 100) AS estimatedtax,
                ir.ignored
            FROM foxybalance_incomerecords ir
            INNER JOIN foxybalance_taxyears ty ON ir.taxyearid = ty.id
        """)

        // Create TaxYearSummaryView
        this.Execute.Sql("""
            CREATE VIEW foxybalance_taxyearsummaryview AS
            SELECT
                v.userid,
                v.taxyearid,
                ty.taxyear,
                ty.taxrate,
                COUNT(v.id)::INT AS totalrecords,
                SUM(v.saleamount)::INT AS totalsales,
                SUM(v.platformfee + v.processingfee)::INT AS totalfees,
                SUM(v.netshare)::INT AS totalnetshare,
                SUM(v.estimatedtax)::NUMERIC AS totalestimatedtax
            FROM foxybalance_incomerecordsview v
            INNER JOIN foxybalance_taxyears ty ON v.taxyearid = ty.id
            WHERE v.ignored = false
            GROUP BY v.userid, v.taxyearid, ty.taxyear, ty.taxrate
        """)

        // Create batch_import_income_records function
        let functionSql = Utils.readSqlFile "batch_import_income_records.sql"
        this.Execute.Sql(functionSql)

    override this.Down() =
        // Drop in reverse order of dependencies
        this.Execute.Sql("DROP FUNCTION IF EXISTS batch_import_income_records(INT, JSONB)")
        this.Execute.Sql("DROP VIEW IF EXISTS foxybalance_taxyearsummaryview")
        this.Execute.Sql("DROP VIEW IF EXISTS foxybalance_incomerecordsview")
        this.Delete.Table("foxybalance_incomerecords") |> ignore
        this.Delete.Table("foxybalance_transactions") |> ignore
        this.Delete.Table("foxybalance_taxyears") |> ignore
        this.Delete.Table("foxybalance_users") |> ignore

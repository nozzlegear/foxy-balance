namespace FoxyBalance.Migrations

open SimpleMigrations

[<Migration(03L, "Add [FoxyBalance_TaxYears] and [FoxyBalance_IncomeRecords]")>]
type Migration_03() =
    inherit Migration() with
        member private x.Run sql =
            x.Execute sql

        override x.Up () =
            Utils.readSqlFileBatches "Migration_03.up.sql"
            |> Seq.iter x.Run

        override x.Down () =
            Utils.readSqlFileBatches "Migration_03.down.sql"
            |> Seq.iter x.Run



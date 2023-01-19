namespace FoxyBalance.Migrations

open SimpleMigrations

[<Migration(06L, "Add [FoxyBalance_TaxYearSummaryView]")>]
type Migration_06() =
    inherit Migration() with
        member private x.Run sql =
            x.Execute sql

        override x.Up () =
            Utils.readSqlFileBatches "Migration_06.up.sql"
            |> Seq.iter x.Run

        override x.Down () =
            Utils.readSqlFileBatches "Migration_06.down.sql"
            |> Seq.iter x.Run
namespace FoxyBalance.Migrations

open SimpleMigrations

[<Migration(05L, "Add [FoxyBalance_IncomeRecordsView]")>]
type Migration_05() =
    inherit Migration() with
        member private x.Run sql =
            x.Execute sql

        override x.Up () =
            Utils.readSqlFileBatches "Migration_05.up.sql"
            |> Seq.iter x.Run

        override x.Down () =
            Utils.readSqlFileBatches "Migration_05.down.sql"
            |> Seq.iter x.Run
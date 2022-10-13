namespace FoxyBalance.Migrations

open SimpleMigrations

[<Migration(04L, "Add [FoxyBalance_IncomeRecords]")>]
type Migration_04() =
    inherit Migration() with
        member private x.Run sql =
            x.Execute sql

        override x.Up () =
            Utils.readSqlFileBatches "Migration_04.up.sql"
            |> Seq.iter x.Run

        override x.Down () =
            Utils.readSqlFileBatches "Migration_04.down.sql"
            |> Seq.iter x.Run
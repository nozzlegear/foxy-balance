namespace FoxyBalance.Database

open System
open FoxyBalance.Database.Models
open FoxyBalance.Database.Interfaces
open Npgsql.FSharp

type RecurringBillDatabase(options : IDatabaseOptions) =
    let connection = Sql.connect options.ConnectionString

    let mapRowToRecurringBill (read : RowReader) : RecurringBill =
        { Id = read.int64 "id"
          Name = read.string "name"
          Amount = read.decimal "amount"
          WeekOfMonth = WeekOfMonth.FromInt(read.int "weekofmonth")
          DayOfWeek = enum<DayOfWeek>(read.int "dayofweek")
          DateCreated = read.datetimeOffset "datecreated"
          LastAppliedDate = read.datetimeOffsetOrNone "lastapplieddate"
          Active = read.bool "active" }

    interface IRecurringBillDatabase with
        member _.GetAsync(userId, billId) =
            task {
                let! results =
                    connection
                    |> Sql.query """
                        SELECT * FROM foxybalance_recurringbills
                        WHERE userid = @userId AND id = @billId
                    """
                    |> Sql.parameters [
                        "userId", Sql.int userId
                        "billId", Sql.int64 billId
                    ]
                    |> Sql.executeAsync mapRowToRecurringBill
                return List.tryHead results
            }

        member _.ListAsync(userId, activeOnly) =
            task {
                let sql =
                    if activeOnly then
                        "SELECT * FROM foxybalance_recurringbills WHERE userid = @userId AND active = true ORDER BY name"
                    else
                        "SELECT * FROM foxybalance_recurringbills WHERE userid = @userId ORDER BY name"

                let! results =
                    connection
                    |> Sql.query sql
                    |> Sql.parameters [ "userId", Sql.int userId ]
                    |> Sql.executeAsync mapRowToRecurringBill
                return Seq.ofList results
            }

        member _.CreateAsync(userId, bill) =
            connection
            |> Sql.query """
                INSERT INTO foxybalance_recurringbills (
                    userid, name, amount, weekofmonth, dayofweek, datecreated, active
                ) VALUES (
                    @userId, @name, @amount, @weekOfMonth, @dayOfWeek, @dateCreated, true
                )
                RETURNING *
            """
            |> Sql.parameters [
                "userId", Sql.int userId
                "name", Sql.string bill.Name
                "amount", Sql.decimal bill.Amount
                "weekOfMonth", Sql.int (bill.WeekOfMonth.ToInt())
                "dayOfWeek", Sql.int (int bill.DayOfWeek)
                "dateCreated", Sql.timestamptz DateTimeOffset.UtcNow
            ]
            |> Sql.executeRowAsync mapRowToRecurringBill

        member _.UpdateAsync(userId, billId, bill) =
            connection
            |> Sql.query """
                UPDATE foxybalance_recurringbills
                SET name = @name,
                    amount = @amount,
                    weekofmonth = @weekOfMonth,
                    dayofweek = @dayOfWeek
                WHERE userid = @userId AND id = @billId
                RETURNING *
            """
            |> Sql.parameters [
                "userId", Sql.int userId
                "billId", Sql.int64 billId
                "name", Sql.string bill.Name
                "amount", Sql.decimal bill.Amount
                "weekOfMonth", Sql.int (bill.WeekOfMonth.ToInt())
                "dayOfWeek", Sql.int (int bill.DayOfWeek)
            ]
            |> Sql.executeRowAsync mapRowToRecurringBill

        member _.UpdateLastAppliedDateAsync(userId, billId, appliedDate) =
            task {
                let! _ =
                    connection
                    |> Sql.query """
                        UPDATE foxybalance_recurringbills
                        SET lastapplieddate = @appliedDate
                        WHERE userid = @userId AND id = @billId
                    """
                    |> Sql.parameters [
                        "userId", Sql.int userId
                        "billId", Sql.int64 billId
                        "appliedDate", Sql.timestamptz (appliedDate : DateTimeOffset)
                    ]
                    |> Sql.executeNonQueryAsync
                return ()
            }

        member _.SetActiveAsync(userId, billId, active) =
            task {
                let! _ =
                    connection
                    |> Sql.query """
                        UPDATE foxybalance_recurringbills
                        SET active = @active
                        WHERE userid = @userId AND id = @billId
                    """
                    |> Sql.parameters [
                        "userId", Sql.int userId
                        "billId", Sql.int64 billId
                        "active", Sql.bool active
                    ]
                    |> Sql.executeNonQueryAsync
                return ()
            }

        member _.DeleteAsync(userId, billId) =
            task {
                let! _ =
                    connection
                    |> Sql.query "DELETE FROM foxybalance_recurringbills WHERE userid = @userId AND id = @billId"
                    |> Sql.parameters [
                        "userId", Sql.int userId
                        "billId", Sql.int64 billId
                    ]
                    |> Sql.executeNonQueryAsync
                return ()
            }

        member _.GetBillsDueForApplicationAsync(currentDate) =
            task {
                let weekStart = currentDate.AddDays(-7.0)

                let! results =
                    connection
                    |> Sql.query """
                        SELECT userid, id, name, amount, weekofmonth, dayofweek, datecreated, lastapplieddate, active
                        FROM foxybalance_recurringbills
                        WHERE active = true
                        AND (
                            lastapplieddate IS NULL
                            OR lastapplieddate < @weekStart
                        )
                    """
                    |> Sql.parameters [
                        "weekStart", Sql.timestamptz (weekStart : DateTimeOffset)
                    ]
                    |> Sql.executeAsync (fun read ->
                        let userId = read.int "userid"
                        let bill = mapRowToRecurringBill read
                        (userId, bill))
                return Seq.ofList results
            }

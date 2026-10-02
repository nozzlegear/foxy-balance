namespace FoxyBalance.Database

open System
open FoxyBalance.Database.Models
open FoxyBalance.Database.Interfaces
open Npgsql.FSharp

type RecurringTransactionDatabase(options : IDatabaseOptions) =
    let connection = Sql.connect options.ConnectionString

    let mapRowToRecurringTransaction (read : RowReader) : RecurringTransaction =
        { Id = read.int64 "id"
          Name = read.string "name"
          Amount = read.decimal "amount"
          Schedule = 
            match (read.int "scheduletype") with
            | 0 -> 
                let sched : WeekOfMonthScheduleDef = { WeekOfMonth = WeekOfMonth.FromInt(read.int "scheduleweekofmonth"); DayOfWeek = enum<System.DayOfWeek>(read.int "scheduledayofweek") }
                ScheduleType.ByWeekOfMonth(sched)
            | 1 -> 
                let sched : ScheduledDateScheduleDef = { Date = read.int "scheduleddate" }
                ScheduleType.ByCalendarDate(sched)
            | _ -> 
                let sched : WeekOfMonthScheduleDef = { WeekOfMonth = WeekOfMonth.FromInt(read.int "scheduleweekofmonth"); DayOfWeek = enum<System.DayOfWeek>(read.int "scheduledayofweek") }
                ScheduleType.ByWeekOfMonth(sched)
          DateCreated = read.datetimeOffset "datecreated"
          LastAppliedDate = read.datetimeOffsetOrNone "lastapplieddate"
          Active = read.bool "active" }

    interface IRecurringTransactionDatabase with
        member _.GetAsync(userId, recurringTransactionId) =
            task {
                let! results =
                    connection
                    |> Sql.query """
                        SELECT * FROM foxybalance_recurringtransactions
                        WHERE userid = @userId AND id = @recurringTransactionId
                    """
                    |> Sql.parameters [
                        "userId", Sql.int userId
                        "recurringTransactionId", Sql.int64 recurringTransactionId
                    ]
                    |> Sql.executeAsync mapRowToRecurringTransaction
                return List.tryHead results
            }

        member _.ListAsync(userId, activeOnly) =
            task {
                let sql =
                    if activeOnly then
                        "SELECT * FROM foxybalance_recurringtransactions WHERE userid = @userId AND active = true ORDER BY name"
                    else
                        "SELECT * FROM foxybalance_recurringtransactions WHERE userid = @userId ORDER BY name"

                let! results =
                    connection
                    |> Sql.query sql
                    |> Sql.parameters [ "userId", Sql.int userId ]
                    |> Sql.executeAsync mapRowToRecurringTransaction
                return Seq.ofList results
            }

        member _.CreateAsync(userId, recurringTransaction) =
            let scheduleType, weekOfMonth, dayOfWeek, scheduledDate =
                match recurringTransaction.Schedule with
                | ByWeekOfMonth wms -> (0, wms.WeekOfMonth.ToInt(), int wms.DayOfWeek, None)
                | ByCalendarDate sd -> (1, 0, 0, Some sd.Date)
            connection
            |> Sql.query """
                INSERT INTO foxybalance_recurringtransactions (
                    userid, name, amount, scheduletype, scheduleweekofmonth, scheduledayofweek, scheduleddate, datecreated, active
                ) VALUES (
                    @userId, @name, @amount, @scheduleType, @weekOfMonth, @dayOfWeek, @scheduledDate, now(), true
                )
                RETURNING *
            """
            |> Sql.parameters [
                "userId", Sql.int userId
                "name", Sql.string recurringTransaction.Name
                "amount", Sql.decimal recurringTransaction.Amount
                "scheduleType", Sql.int scheduleType
                "weekOfMonth", Sql.int weekOfMonth
                "dayOfWeek", Sql.int dayOfWeek
                "scheduledDate", match scheduledDate with Some d -> Sql.int d | None -> Sql.dbnull
            ]
            |> Sql.executeRowAsync mapRowToRecurringTransaction

        member _.UpdateAsync(userId, recurringTransactionId, recurringTransaction) =
            let scheduleType, weekOfMonth, dayOfWeek, scheduledDate =
                match recurringTransaction.Schedule with
                | ByWeekOfMonth wms -> (0, wms.WeekOfMonth.ToInt(), int wms.DayOfWeek, None)
                | ByCalendarDate sd -> (1, 0, 0, Some sd.Date)
            connection
            |> Sql.query """
                UPDATE foxybalance_recurringtransactions
                SET name = @name,
                    amount = @amount,
                    scheduletype = @scheduleType,
                    scheduleweekofmonth = @weekOfMonth,
                    scheduledayofweek = @dayOfWeek,
                    scheduleddate = @scheduledDate
                WHERE userid = @userId AND id = @recurringTransactionId
                RETURNING *
            """
            |> Sql.parameters [
                "userId", Sql.int userId
                "recurringTransactionId", Sql.int64 recurringTransactionId
                "name", Sql.string recurringTransaction.Name
                "amount", Sql.decimal recurringTransaction.Amount
                "scheduleType", Sql.int scheduleType
                "weekOfMonth", Sql.int weekOfMonth
                "dayOfWeek", Sql.int dayOfWeek
                "scheduledDate", match scheduledDate with Some d -> Sql.int d | None -> Sql.dbnull
            ]
            |> Sql.executeRowAsync mapRowToRecurringTransaction

        member _.UpdateLastAppliedDateAsync(userId, recurringTransactionId, appliedDate) =
            task {
                let! _ =
                    connection
                    |> Sql.query """
                        UPDATE foxybalance_recurringtransactions
                        SET lastapplieddate = @appliedDate
                        WHERE userid = @userId AND id = @recurringTransactionId
                    """
                    |> Sql.parameters [
                        "userId", Sql.int userId
                        "recurringTransactionId", Sql.int64 recurringTransactionId
                        "appliedDate", Sql.timestamptz (appliedDate : DateTimeOffset)
                    ]
                    |> Sql.executeNonQueryAsync
                return ()
            }

        member _.SetActiveAsync(userId, recurringTransactionId, active) =
            task {
                let! _ =
                    connection
                    |> Sql.query """
                        UPDATE foxybalance_recurringtransactions
                        SET active = @active
                        WHERE userid = @userId AND id = @recurringTransactionId
                    """
                    |> Sql.parameters [
                        "userId", Sql.int userId
                        "recurringTransactionId", Sql.int64 recurringTransactionId
                        "active", Sql.bool active
                    ]
                    |> Sql.executeNonQueryAsync
                return ()
            }

        member _.DeleteAsync(userId, recurringTransactionId) =
            task {
                let! _ =
                    connection
                    |> Sql.query "DELETE FROM foxybalance_recurringtransactions WHERE userid = @userId AND id = @recurringTransactionId"
                    |> Sql.parameters [
                        "userId", Sql.int userId
                        "recurringTransactionId", Sql.int64 recurringTransactionId
                    ]
                    |> Sql.executeNonQueryAsync
                return ()
            }

        member _.GetRecurringTransactionsDueForApplicationAsync(currentDate) =
            task {
                let weekStart = currentDate.AddDays(-7.0)

                let! results =
                    connection
                    |> Sql.query """
                        SELECT userid, id, name, amount, scheduletype, scheduleweekofmonth, scheduledayofweek, scheduleddate, datecreated, lastapplieddate, active
                        FROM foxybalance_recurringtransactions
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
                        let rt = mapRowToRecurringTransaction read
                        (userId, rt))
                return Seq.ofList results
            }
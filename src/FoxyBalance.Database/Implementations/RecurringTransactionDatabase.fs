namespace FoxyBalance.Database

open System
open FoxyBalance.Database.Models
open FoxyBalance.Database.Interfaces
open Npgsql.FSharp

type RecurringTransactionDatabase(options : IDatabaseOptions) =
    let connection = Sql.connect options.ConnectionString

    let mapRowToRecurringTransaction (read : RowReader) : RecurringTransaction =
        let recurringType =
            match read.stringOrNone "recurringtype" with
            | Some "income" -> RecurringTransactionType.Income
            | _ -> RecurringTransactionType.Bill
        { Id = read.int64 "id"
          Name = read.string "name"
          Amount = read.decimal "amount"
          Schedule = 
            match (read.int "scheduletype") with
            | 0 -> 
                let sched : WeekOfMonthScheduleDef = { WeekOfMonth = WeekOfMonth.FromInt(read.int "scheduleweekofmonth"); DayOfWeek = enum<System.DayOfWeek>(read.int "scheduledayofweek") }
                ScheduleType.ByWeekOfMonth(sched)
            | 1 ->
                let sched : ScheduledDateScheduleDef =
                    { Date = read.int "scheduleddate"
                      Apply = read.stringOrNone "scheduleapplydate" |> Option.bind ApplyDate.FromDbString }
                ScheduleType.ByCalendarDate(sched)
            | 2 ->
                let unit =
                    match read.stringOrNone "byintervalunit" with
                    | Some "week" -> IntervalUnit.Week
                    | Some "month" -> IntervalUnit.Month
                    | _ -> IntervalUnit.Day
                let count = read.intOrNone "byintervalcount" |> Option.defaultValue 1
                ScheduleType.ByInterval { Count = count; Unit = unit }
            | _ -> 
                let sched : WeekOfMonthScheduleDef = { WeekOfMonth = WeekOfMonth.FromInt(read.int "scheduleweekofmonth"); DayOfWeek = enum<System.DayOfWeek>(read.int "scheduledayofweek") }
                ScheduleType.ByWeekOfMonth(sched)
          Type = recurringType
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
            let scheduleType, weekOfMonth, dayOfWeek, scheduledDate, applyDate, intervalCount, intervalUnit =
                match recurringTransaction.Schedule with
                | ByWeekOfMonth wms -> (0, wms.WeekOfMonth.ToInt(), int wms.DayOfWeek, None, None, None, None)
                | ByCalendarDate sd -> (1, 0, 0, Some sd.Date, sd.Apply |> Option.map (fun a -> a.ToDbString()), None, None)
                | ByInterval iv ->
                    let u = match iv.Unit with | Day -> "day" | Week -> "week" | Month -> "month"
                    (2, 0, 0, None, None, Some iv.Count, Some u)
            connection
            |> Sql.query """
                INSERT INTO foxybalance_recurringtransactions (
                    userid, name, amount, scheduletype, scheduleweekofmonth, scheduledayofweek, scheduleddate, scheduleapplydate, byintervalcount, byintervalunit, recurringtype, datecreated, active
                ) VALUES (
                    @userId, @name, @amount, @scheduleType, @weekOfMonth, @dayOfWeek, @scheduledDate, @scheduleApplyDate, @byIntervalCount, @byIntervalUnit, @recurringType, now(), true
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
                "scheduleApplyDate", match applyDate with Some a -> Sql.string a | None -> Sql.dbnull
                "byIntervalCount", match intervalCount with Some n -> Sql.int n | None -> Sql.dbnull
                "byIntervalUnit", match intervalUnit with Some u -> Sql.string u | None -> Sql.dbnull
                "recurringType", Sql.string (match recurringTransaction.Type with | RecurringTransactionType.Bill -> "bill" | RecurringTransactionType.Income -> "income")
            ]
            |> Sql.executeRowAsync mapRowToRecurringTransaction

        member _.UpdateAsync(userId, recurringTransactionId, recurringTransaction) =
            let scheduleType, weekOfMonth, dayOfWeek, scheduledDate, applyDate, intervalCount, intervalUnit =
                match recurringTransaction.Schedule with
                | ByWeekOfMonth wms -> (0, wms.WeekOfMonth.ToInt(), int wms.DayOfWeek, None, None, None, None)
                | ByCalendarDate sd -> (1, 0, 0, Some sd.Date, sd.Apply |> Option.map (fun a -> a.ToDbString()), None, None)
                | ByInterval iv ->
                    let u = match iv.Unit with | Day -> "day" | Week -> "week" | Month -> "month"
                    (2, 0, 0, None, None, Some iv.Count, Some u)
            connection
            |> Sql.query """
                UPDATE foxybalance_recurringtransactions
                SET name = @name,
                    amount = @amount,
                    scheduletype = @scheduleType,
                    scheduleweekofmonth = @weekOfMonth,
                    scheduledayofweek = @dayOfWeek,
                    scheduleddate = @scheduledDate,
                    scheduleapplydate = @scheduleApplyDate,
                    byintervalcount = @byIntervalCount,
                    byintervalunit = @byIntervalUnit,
                    recurringtype = @recurringType
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
                "scheduleApplyDate", match applyDate with Some a -> Sql.string a | None -> Sql.dbnull
                "byIntervalCount", match intervalCount with Some n -> Sql.int n | None -> Sql.dbnull
                "byIntervalUnit", match intervalUnit with Some u -> Sql.string u | None -> Sql.dbnull
                "recurringType", Sql.string (match recurringTransaction.Type with | RecurringTransactionType.Bill -> "bill" | RecurringTransactionType.Income -> "income")
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
                        SELECT userid, id, name, amount, scheduletype, scheduleweekofmonth, scheduledayofweek, scheduleddate, scheduleapplydate, byintervalcount, byintervalunit, recurringtype, datecreated, lastapplieddate, active
                        FROM foxybalance_recurringtransactions
                        WHERE active = true
                        AND (
                            -- Month-based schedules: only re-evaluate if not applied in the last week.
                            lastapplieddate IS NULL
                            OR lastapplieddate < @weekStart
                            -- Interval schedules may be due more often than weekly (e.g. every 2 days),
                            -- so always include them; shouldApplyNow decides based on LastAppliedDate.
                            OR scheduletype = 2
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
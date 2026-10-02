namespace FoxyBalance.Server.Services

open System
open FoxyBalance.Database.Models

module RecurringSchedule =

    /// Calculate the target date for a given schedule within the month of `referenceDate`.
    /// For `ByCalendarDate`, returns `referenceDate.Year/Month/Date`, skipping the month if
    /// `Date` exceeds that month's day count.
    /// For `ByWeekOfMonth`, returns the Nth occurrence of the specified day of week in that month.
    let targetDateForMonth (schedule : ScheduleType) (referenceDate : DateTimeOffset) : DateTimeOffset option =
        match schedule with
        | ScheduleType.ByCalendarDate sched ->
            let daysInMonth = DateTime.DaysInMonth(referenceDate.Year, referenceDate.Month)
            if sched.Date > daysInMonth then
                None
            else
                DateTimeOffset(referenceDate.Year, referenceDate.Month, sched.Date, 0, 0, 0, referenceDate.Offset)
                |> Some
        | ScheduleType.ByWeekOfMonth sched ->
            let firstDayOfMonth = DateTimeOffset(referenceDate.Year, referenceDate.Month, 1, 0, 0, 0, referenceDate.Offset)
            let daysUntilTargetDay = (int sched.DayOfWeek - int firstDayOfMonth.DayOfWeek + 7) % 7
            let firstTargetDayOfMonth = firstDayOfMonth.AddDays(float daysUntilTargetDay)
            let weeksToAdd = sched.WeekOfMonth.ToInt() - 1
            firstTargetDayOfMonth.AddDays(float (weeksToAdd * 7))
            |> Some

    /// Enumerate all occurrences of `schedule` that fall within `[from, to]` (inclusive on both ends),
    /// ascending. For each calendar month overlapping the range, yields `targetDateForMonth` when it
    /// falls within the range.
    let enumerateOccurrences (schedule : ScheduleType) (from : DateTimeOffset) (to' : DateTimeOffset) : DateTimeOffset list =
        // Normalize to date-only (midnight) to avoid time-of-day comparison issues.
        let fromDate = from.Date
        let toDate = to'.Date
        let offset = from.Offset

        // Iterate over each month from `from` to `to'`.
        let rec loop (year, month) acc =
            let candidateOpt = targetDateForMonth schedule (DateTimeOffset(year, month, 1, 0, 0, 0, offset))
            let nextAcc =
                match candidateOpt with
                | Some candidate ->
                    let candidateDate = candidate.Date
                    if candidateDate >= fromDate && candidateDate <= toDate then
                        candidate :: acc
                    else
                        acc
                | None ->
                    acc
            let nextYear, nextMonth =
                if month = 12 then (year + 1, 1) else (year, month + 1)
            // Stop when we've passed the `to` month
            let nextMonthStart = DateTime(nextYear, nextMonth, 1)
            if nextMonthStart > toDate then
                nextAcc
            else
                loop (nextYear, nextMonth) nextAcc

        let startYear, startMonth = fromDate.Year, fromDate.Month
        loop (startYear, startMonth) []
        |> List.sort

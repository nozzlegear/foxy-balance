namespace FoxyBalance.Server.Services

open System
open FoxyBalance.Database.Models

module RecurringSchedule =

    /// Calculate the target date for a given schedule within the month of `referenceDate`.
    /// For `ByCalendarDate`:
    ///   - if `Date` exists in the month, use it directly;
    ///   - if `Date` exceeds the month's day count and `Apply` is set, fall back to the last
    ///     day of the month (LastDayOfMonth) or the 1st of the next month (NextMonth1st);
    ///   - otherwise (Apply = None), skip the month (return None) — legacy behavior.
    /// For `ByWeekOfMonth`, returns the Nth occurrence of the specified day of week in that month.
    let targetDateForMonth (schedule : ScheduleType) (referenceDate : DateTimeOffset) : DateTimeOffset option =
        match schedule with
        | ScheduleType.ByCalendarDate sched ->
            let daysInMonth = DateTime.DaysInMonth(referenceDate.Year, referenceDate.Month)
            if sched.Date <= daysInMonth then
                DateTimeOffset(referenceDate.Year, referenceDate.Month, sched.Date, 0, 0, 0, referenceDate.Offset)
                |> Some
            else
                match sched.Apply with
                | Some LastDayOfMonth ->
                    DateTimeOffset(referenceDate.Year, referenceDate.Month, daysInMonth, 0, 0, 0, referenceDate.Offset)
                    |> Some
                | Some NextMonth1st ->
                    let next = referenceDate.AddMonths(1)
                    DateTimeOffset(next.Year, next.Month, 1, 0, 0, 0, referenceDate.Offset)
                    |> Some
                | None ->
                    None
        | ScheduleType.ByWeekOfMonth sched ->
            let firstDayOfMonth = DateTimeOffset(referenceDate.Year, referenceDate.Month, 1, 0, 0, 0, referenceDate.Offset)
            let daysUntilTargetDay = (int sched.DayOfWeek - int firstDayOfMonth.DayOfWeek + 7) % 7
            let firstTargetDayOfMonth = firstDayOfMonth.AddDays(float daysUntilTargetDay)
            let weeksToAdd = sched.WeekOfMonth.ToInt() - 1
            firstTargetDayOfMonth.AddDays(float (weeksToAdd * 7))
            |> Some
        | ScheduleType.ByInterval _ ->
            // ByInterval doesn't work on a per-month basis.
            // targetDateForMonth is not meaningful for ByInterval.
            None

    /// For a ByInterval schedule, compute the next due date given the last applied date.
    /// Returns (isDue, dueDate): isDue is true when currentDate is on or after the due date.
    let dueDateByInterval (count: int) (unit: IntervalUnit) (lastApplied: DateTimeOffset option) (currentDate: DateTimeOffset) : bool * DateTimeOffset =
        let nextDue =
            match lastApplied with
            | None -> currentDate
            | Some last ->
                match unit with
                | Day -> last.AddDays(float count)
                | Week -> last.AddDays(float (count * 7))
                | Month -> last.AddMonths(count)
        let due = currentDate.Date >= nextDue.Date
        let stampDate = if nextDue.Date <= currentDate.Date then nextDue else currentDate
        (due, stampDate)

    /// Enumerate occurrences for ByInterval schedule type.
    /// Steps by the interval from a starting point and collects all dates within [from, to].
    let private enumerateOccurrencesByInterval (count: int) (unit: IntervalUnit) (from : DateTimeOffset) (to' : DateTimeOffset) : DateTimeOffset list =
        let fromDate = from.Date
        let toDate = to'.Date
        // Step from the anchor by index so month arithmetic doesn't drift
        // (chaining AddMonths would clamp, e.g. Jan 31 + 3mo = Apr 30, + 3mo = Jul 30).
        let step (i: int) =
            match unit with
            | Day -> from.AddDays(float (count * i))
            | Week -> from.AddDays(float (count * 7 * i))
            | Month -> from.AddMonths(count * i)
        let rec loop (i: int) acc =
            let current = step i
            let currentDate = current.Date
            if currentDate > toDate then acc
            else
                let nextAcc = if currentDate >= fromDate then current :: acc else acc
                loop (i + 1) nextAcc
        loop 0 []
        |> List.sort

    /// Enumerate all occurrences of `schedule` that fall within `[from, to]` (inclusive on both ends),
    /// ascending. For each calendar month overlapping the range, yields `targetDateForMonth` when it
    /// falls within the range.
    let enumerateOccurrences (schedule : ScheduleType) (from : DateTimeOffset) (to' : DateTimeOffset) : DateTimeOffset list =
        match schedule with
        | ScheduleType.ByInterval { Count = count; Unit = unit } ->
            enumerateOccurrencesByInterval count unit from to'
        | _ ->
            // For ByWeekOfMonth and ByCalendarDate, use the month-looping approach
            let fromDate = from.Date
            let toDate = to'.Date
            let offset = from.Offset

            let rec monthLoop (year, month) acc =
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
                let nextMonthStart = DateTime(nextYear, nextMonth, 1)
                if nextMonthStart > toDate then nextAcc
                else monthLoop (nextYear, nextMonth) nextAcc

            let startYear, startMonth = fromDate.Year, fromDate.Month
            monthLoop (startYear, startMonth) []
            |> List.sort

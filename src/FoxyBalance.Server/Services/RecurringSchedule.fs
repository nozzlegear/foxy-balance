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

    /// Step function for a ByInterval schedule: the k-th occurrence anchored at `startDate`.
    /// Steps by index so month arithmetic doesn't drift
    /// (chaining AddMonths would clamp, e.g. Jan 31 + 3mo = Apr 30, + 3mo = Jul 30).
    let private intervalStep (count: int) (unit: IntervalUnit) (startDate: DateTimeOffset) (i: int) : DateTimeOffset =
        match unit with
        | Day -> startDate.AddDays(float (count * i))
        | Week -> startDate.AddDays(float (count * 7 * i))
        | Month -> startDate.AddMonths(count * i)

    /// For a ByInterval schedule anchored at `startDate`, compute the next due date given the
    /// last applied date. Returns (isDue, dueDate): isDue is true when currentDate is on or
    /// after the due date; dueDate is the occurrence date when due, otherwise currentDate.
    /// Never applied: due iff currentDate >= startDate (the first occurrence gates application).
    /// Otherwise: due iff the smallest occurrence strictly after lastApplied has arrived.
    let dueDateByInterval (count: int) (unit: IntervalUnit) (startDate: DateTimeOffset) (lastApplied: DateTimeOffset option) (currentDate: DateTimeOffset) : bool * DateTimeOffset =
        let step = intervalStep count unit startDate
        let nextDue =
            match lastApplied with
            | None -> step 0
            | Some last ->
                // Estimate a starting index near lastApplied, then correct in both directions
                // (month clamping makes the estimate inexact, e.g. Jan 31 anchor).
                let est =
                    match unit with
                    | Day -> max 0 ((last.Date - startDate.Date).Days / count)
                    | Week -> max 0 ((last.Date - startDate.Date).Days / (count * 7))
                    | Month -> max 0 (((last.Year - startDate.Year) * 12 + (last.Month - startDate.Month)) / count)
                let rec walkUp k = if (step k).Date > last.Date then k else walkUp (k + 1)
                let rec walkDown k = if k > 0 && (step (k - 1)).Date > last.Date then walkDown (k - 1) else k
                step (walkDown (walkUp est))
        let due = currentDate.Date >= nextDue.Date
        let stampDate = if due then nextDue else currentDate
        (due, stampDate)

    /// Enumerate occurrences for ByInterval schedule type.
    /// Steps by the interval from the schedule's start date and collects all occurrences
    /// within [from, to].
    let private enumerateOccurrencesByInterval (count: int) (unit: IntervalUnit) (startDate: DateTimeOffset) (from : DateTimeOffset) (to' : DateTimeOffset) : DateTimeOffset list =
        let fromDate = from.Date
        let toDate = to'.Date
        let step = intervalStep count unit startDate
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
        | ScheduleType.ByInterval { Count = count; Unit = unit; StartDate = startDate } ->
            enumerateOccurrencesByInterval count unit startDate from to'
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

namespace FoxyBalance.Database.Tests

open System
open FoxyBalance.Database.Models
open FoxyBalance.Server.Services
open Xunit

/// Pure unit tests for RecurringSchedule, focused on the ByInterval schedule type.
/// No database required.
module RecurringScheduleTests =

    let private dto (y: int) (m: int) (d: int) = DateTimeOffset(y, m, d, 0, 0, 0, TimeSpan.Zero)

    let private dates (occ: DateTimeOffset list) = occ |> List.map (fun d -> d.Date) |> List.map (fun d -> d.ToString("yyyy-MM-dd"))

    // ---- enumerateOccurrences: explicit expected dates ----

    let getByIntervalOccurences() = [
        1, "day", "2026-01-01", "2026-01-07", "2026-01-01,2026-01-02,2026-01-03,2026-01-04,2026-01-05,2026-01-06,2026-01-07"
        2, "day", "2026-01-01", "2026-01-10", "2026-01-01,2026-01-03,2026-01-05,2026-01-07,2026-01-09"
        7, "day", "2026-01-01", "2026-02-01", "2026-01-01,2026-01-08,2026-01-15,2026-01-22,2026-01-29"
        1, "week", "2026-01-01", "2026-02-01", "2026-01-01,2026-01-08,2026-01-15,2026-01-22,2026-01-29"
        2, "week", "2026-01-01", "2026-03-01", "2026-01-01,2026-01-15,2026-01-29,2026-02-12,2026-02-26"
        4, "week", "2026-01-01", "2026-03-01", "2026-01-01,2026-01-29,2026-02-26"
        1, "month", "2026-01-15", "2026-06-30", "2026-01-15,2026-02-15,2026-03-15,2026-04-15,2026-05-15,2026-06-15"
        2, "month", "2026-01-31", "2026-08-01", "2026-01-31,2026-03-31,2026-05-31,2026-07-31"
        3, "month", "2026-01-31", "2026-12-31", "2026-01-31,2026-04-30,2026-07-31,2026-10-31"
        2, "week", "2026-11-05", "2026-12-31", "2026-11-05,2026-11-19,2026-12-03,2026-12-17,2026-12-31"
    ]

    [<Theory>]
    [<MemberData(nameof getByIntervalOccurences)>]
    let ``enumerateOccurrences ByInterval produces expected dates`` (
        
        count: int, unitStr: string, fromStr: string, toStr: string, expectedCsv: string
    ) =
        let unit =
            match unitStr with
            | "day" -> IntervalUnit.Day
            | "week" -> IntervalUnit.Week
            | "month" -> IntervalUnit.Month
            | _ -> failwith "bad unit"
        let from = DateTimeOffset.Parse(fromStr).ToUniversalTime() |> fun d -> DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero)
        let to' = DateTimeOffset.Parse(toStr).ToUniversalTime() |> fun d -> DateTimeOffset(d.Year, d.Month, d.Day, 0, 0, 0, TimeSpan.Zero)
        let schedule = ScheduleType.ByInterval { Count = count; Unit = unit }

        let occ = RecurringSchedule.enumerateOccurrences schedule from to'

        Assert.Equal(expectedCsv, String.Join(",", dates occ))

    // ---- enumerateOccurrences: combinatorial spacing invariants ----

    [<Theory>]
    [<CombinatorialData>]
    let ``enumerateOccurrences ByInterval day/week occurrences are evenly spaced``
        ([<CombinatorialValues(1, 2, 3, 7, 14)>] count: int)
        ([<CombinatorialValues("day", "week")>] unitStr: string) =
        let unit = if unitStr = "day" then IntervalUnit.Day else IntervalUnit.Week
        let expectedStepDays = if unitStr = "day" then count else count * 7
        let from = dto 2026 1 1
        let to' = dto 2026 4 1
        let schedule = ScheduleType.ByInterval { Count = count; Unit = unit }

        let occ = RecurringSchedule.enumerateOccurrences schedule from to'

        // First occurrence is the from date itself; all within range; evenly spaced.
        Assert.Equal(from.Date, (List.head occ).Date)
        Assert.All(occ, fun d -> Assert.InRange(d.Date, from.Date, to'.Date))
        occ |> List.pairwise |> List.iter (fun (a, b) -> Assert.Equal(expectedStepDays, (b.Date - a.Date).Days))

    [<Theory>]
    [<CombinatorialData>]
    let ``enumerateOccurrences ByInterval month occurrences step by whole months``
        ([<CombinatorialValues(1, 2, 3, 6, 12)>] count: int) =
        let from = dto 2026 1 31
        let to' = dto 2027 12 31
        let schedule = ScheduleType.ByInterval { Count = count; Unit = IntervalUnit.Month }

        let occ = RecurringSchedule.enumerateOccurrences schedule from to'

        Assert.Equal(from.Date, (List.head occ).Date)
        occ
        |> List.pairwise
        |> List.iter (fun (a, b) ->
            let expectedMonths = count
            let actualMonths = (b.Year - a.Year) * 12 + (b.Month - a.Month)
            Assert.Equal(expectedMonths, actualMonths))

    [<Fact>]
    let ``ByInterval every 2 weeks starting Nov 5 2026 yields 2 in November and 3 in December`` () =
        let from = dto 2026 11 5
        let to' = dto 2026 12 31
        let schedule = ScheduleType.ByInterval { Count = 2; Unit = IntervalUnit.Week }

        let occ = RecurringSchedule.enumerateOccurrences schedule from to'

        Assert.Equal("2026-11-05,2026-11-19,2026-12-03,2026-12-17,2026-12-31", String.Join(",", dates occ))
        let byMonth = occ |> List.countBy (fun d -> (d.Year, d.Month)) |> Map.ofList
        Assert.Equal(2, byMonth.[(2026, 11)])
        Assert.Equal(3, byMonth.[(2026, 12)])

    // ---- range boundaries are inclusive ----

    [<Fact>]
    let ``enumerateOccurrences ByInterval includes both range endpoints when they land on occurrences`` () =
        let from = dto 2026 3 1
        let to' = dto 2026 3 15
        let occ = RecurringSchedule.enumerateOccurrences (ScheduleType.ByInterval { Count = 7; Unit = IntervalUnit.Day }) from to'
        Assert.Equal("2026-03-01,2026-03-08,2026-03-15", String.Join(",", dates occ))

    [<Fact>]
    let ``enumerateOccurrences ByInterval empty when range shorter than interval`` () =
        let from = dto 2026 3 1
        let to' = dto 2026 3 5
        let occ = RecurringSchedule.enumerateOccurrences (ScheduleType.ByInterval { Count = 10; Unit = IntervalUnit.Day }) from to'
        Assert.Equal("2026-03-01", String.Join(",", dates occ))

    // ---- existing schedule types still work (regression) ----

    [<Fact>]
    let ``enumerateOccurrences ByWeekOfMonth still enumerates one date per month`` () =
        let from = dto 2026 1 1
        let to' = dto 2026 3 31
        let schedule = ScheduleType.ByWeekOfMonth { WeekOfMonth = FirstWeek; DayOfWeek = DayOfWeek.Monday }
        let occ = RecurringSchedule.enumerateOccurrences schedule from to'
        Assert.Equal(3, List.length occ)

    [<Fact>]
    let ``enumerateOccurrences ByCalendarDate still enumerates one date per month`` () =
        let from = dto 2026 1 1
        let to' = dto 2026 3 31
        let schedule = ScheduleType.ByCalendarDate { Date = 15; Apply = None }
        let occ = RecurringSchedule.enumerateOccurrences schedule from to'
        Assert.Equal("2026-01-15,2026-02-15,2026-03-15", String.Join(",", dates occ))

    // ---- dueDateByInterval ----

    let getDueDateCases() = [
        // count, unit, lastApplied (null = never), current, expectedDue, expectedStamp
        2, "day", null, "2026-03-10", true, "2026-03-10"            // never applied -> due now
        2, "day", "2026-03-01", "2026-03-02", false, "2026-03-02"    // due 03-03, not yet; stamp = today
        2, "day", "2026-03-01", "2026-03-03", true, "2026-03-03"     // due 03-03, today
        2, "day", "2026-03-01", "2026-03-10", true, "2026-03-03"     // overdue; stamp with due date
        1, "week", "2026-03-01", "2026-03-07", false, "2026-03-07"  // due 03-08, not yet
        1, "week", "2026-03-01", "2026-03-08", true, "2026-03-08"   // due 03-08, today
        1, "month", "2026-01-31", "2026-02-27", false, "2026-02-27" // due 02-28 (Feb 31 clamps), not yet
        1, "month", "2026-01-31", "2026-02-28", true, "2026-02-28"  // due 02-28, today
    ]

    [<Theory>]
    [<MemberData(nameof getDueDateCases)>]
    let ``dueDateByInterval computes due flag and stamp date`` (
        
        count: int, unitStr: string, lastAppliedStr: string, currentStr: string, expectedDue: bool, expectedStampStr: string
    ) =
        let unit =
            match unitStr with
            | "day" -> IntervalUnit.Day
            | "week" -> IntervalUnit.Week
            | "month" -> IntervalUnit.Month
            | _ -> failwith "bad unit"
        let lastApplied =
            match lastAppliedStr with
            | null -> None
            | s -> Some (DateTimeOffset.Parse(s + "T00:00:00Z"))
        let current = DateTimeOffset.Parse(currentStr + "T00:00:00Z")

        let (due, stamp) = RecurringSchedule.dueDateByInterval count unit lastApplied current

        Assert.Equal(expectedDue, due)
        Assert.Equal(DateTimeOffset.Parse(expectedStampStr + "T00:00:00Z").Date, stamp.Date)

namespace FoxyBalance.Server.Services

open System
open System.Globalization
open System.Threading.Tasks
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open FoxyBalance.Server.Models.ViewModels

type ForecastService(
    transactionDb: ITransactionDatabase,
    recurringTransactionDb: IRecurringTransactionDatabase) =

    /// Validate the date range query parameters. Pure function — no DB access.
    /// Returns Ok(StartDate, EndDate) or Error(errorMessage).
    static member validate (startDateStr : string) (endDateStr : string) (today : DateTimeOffset) : Result<{| StartDate : DateTimeOffset; EndDate : DateTimeOffset |}, string> =
        let tryParseDate (s : string) =
            match DateTimeOffset.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None) with
            | true, d -> Some d
            | false, _ -> None

        match tryParseDate startDateStr, tryParseDate endDateStr with
        | None, _ | _, None ->
            Error "Please enter a valid start and end date (yyyy-MM-dd)."
        | Some startDate, Some endDate ->
            // startDate must be at least 1 day before endDate
            if startDate.Date >= endDate.Date then
                Error "Start date must be at least 1 day before the end date."
            // endDate must be >= today + 1 day
            elif endDate.Date < today.Date.AddDays(1.0) then
                Error "End date must be at least 1 day in the future."
            // span cannot exceed 365 days (366 distinct days = 1 year + 1 day)
            elif (endDate.Date - startDate.Date).Days > 365 then
                Error "Forecast cannot be longer than 1 year and 1 day."
            else
                Ok {| StartDate = startDate; EndDate = endDate |}

    /// Sign an amount based on transaction type: Credit → +, everything else → −.
    static member private signedAmount (amount : decimal) (txType : TransactionType) : decimal =
        match txType with
        | Credit -> amount
        | _ -> -amount

    /// Label for the transaction type, used in the table's "Type" column.
    static member private typeLabel (txType : TransactionType) : string =
        match txType with
        | Credit -> "Income"
        | Bill _ -> "Bill"
        | Check _ -> "Check"
        | Debit -> "Debit"

    /// Build the full forecast: backfill real transactions then project recurring ones.
    /// The `now` parameter defaults to UtcNow so tests can anchor it deterministically.
    /// `includedEventIds` when Some determines which events are toggled on (off = excluded from calc).
    /// `temporaryRecurringTransactions` are forecast-only recurring bills never persisted to DB.
    member this.BuildForecastAsync(userId : UserId, startDate : DateTimeOffset, endDate : DateTimeOffset,
                                   ?now : DateTimeOffset,
                                   ?includedEventIds : Set<string>,
                                   ?temporaryRecurringTransactions : TempRecurringTransaction list)
                                   : Task<ForecastViewModel> =
        let now = defaultArg now DateTimeOffset.UtcNow
        let todayDate = now.Date
        let tempItems = defaultArg temporaryRecurringTransactions []
        task {
            // 1. Starting balance: all transactions before startDate, including pending.
            let! startingSum = transactionDb.SumAsOfDateAsync(userId, startDate, true)
            let startingBalance = startingSum.Sum

            // 2. Real transactions in [startDate, endDate].
            let! realTxns = transactionDb.ListInRangeAsync(userId, startDate, endDate)
            let realEvents =
                realTxns
                |> Seq.toList
                |> List.map (fun t ->
                    {| Date = t.DateCreated
                       Label = ForecastService.typeLabel t.Type
                       Description = t.Name
                       Amount = Some (ForecastService.signedAmount t.Amount t.Type)
                       EventId = sprintf "t%i" t.Id
                       IsTemporary = false |})

            // 3. Predicted recurring transactions: active ones, occurrences > today, in [startDate, endDate].
            let! recs = recurringTransactionDb.ListAsync(userId, true)
            let recsList = Seq.toList recs
            let predictedEvents =
                recsList
                |> List.collect (fun rt ->
                    RecurringSchedule.enumerateOccurrences rt.Schedule startDate endDate
                    |> List.filter (fun d -> d.Date > todayDate)
                    |> List.map (fun d ->
                        {| Date = d
                           Label = "Bill"
                           Description = rt.Name
                           Amount = Some (-rt.Amount)
                           EventId = sprintf "r%i-%s" rt.Id (Format.date d)
                           IsTemporary = false |}))

            // 3b. Temporary recurring transactions (forecast-only, never persisted).
            let tempEvents =
                tempItems
                |> List.indexed
                |> List.collect (fun (i, t) ->
                    RecurringSchedule.enumerateOccurrences t.Schedule startDate endDate
                    |> List.filter (fun d -> d.Date > todayDate)
                    |> List.map (fun d ->
                        {| Date = d
                           Label = "Bill"
                           Description = t.Name
                           Amount = Some -t.Amount
                           EventId = sprintf "x%i-%s" i (Format.date d)
                           IsTemporary = true |}))

            // 4. Sort ascending by date (stable; real before predicted on ties).
            let allEvents =
                realEvents @ predictedEvents @ tempEvents
                |> List.sortBy (fun e -> e.Date.DateTime)

            // 5. Build rows and chart arrays by folding from starting balance.
            // Toggled-off events: balance unchanged (row shows carried balance), chart entry skipped.
            let isToggledOff (eventId : string) (isTemporary : bool) =
                if isTemporary then false
                else
                    match includedEventIds with
                    | None -> false
                    | Some ids -> not (ids.Contains eventId)

            let rowsWithBalances =
                allEvents
                |> List.fold (fun (rows, balance, chartDates, chartBalances) e ->
                    let toggledOff = isToggledOff e.EventId e.IsTemporary
                    let newBalance =
                        if toggledOff then balance
                        else balance + e.Amount.Value
                    let row =
                        { Date = e.Date
                          Label = e.Label
                          Description = e.Description
                          Amount = e.Amount
                          RunningBalance = newBalance
                          EventId = Some e.EventId
                          IsToggledOff = toggledOff
                          IsTemporary = e.IsTemporary }
                    let chartDate = Format.date e.Date
                    let nextRows = rows @ [row]
                    if toggledOff then
                        (nextRows, newBalance, chartDates, chartBalances)
                    else
                        let nextChartDates = chartDates @ [chartDate]
                        let nextChartBalances = chartBalances @ [newBalance]
                        (nextRows, newBalance, nextChartDates, nextChartBalances)
                ) ([], startingBalance, [Format.date startDate], [startingBalance])

            let (eventRows, finalBalance, chartDates, chartBalances) = rowsWithBalances

            // 6. Leading "Starting balance" row and trailing "Projected end-date balance" row.
            let startingRow =
                { Date = startDate
                  Label = "Starting balance"
                  Description = ""
                  Amount = None
                  RunningBalance = startingBalance
                  EventId = None
                  IsToggledOff = false
                  IsTemporary = false }

            let projectedRow =
                { Date = endDate
                  Label = "Projected end-date balance"
                  Description = ""
                  Amount = None
                  RunningBalance = finalBalance
                  EventId = None
                  IsToggledOff = false
                  IsTemporary = false }

            let allRows = [startingRow] @ eventRows @ [projectedRow]

            let model : ForecastViewModel =
                { Error = None
                  StartDateStr = Format.date startDate
                  EndDateStr = Format.date endDate
                  StartDate = startDate
                  EndDate = endDate
                  StartingBalance = startingBalance
                  Rows = allRows
                  ChartDates = chartDates
                  ChartBalances = chartBalances
                  TmpJson = ""
                  TempItems = []
                  TempItemForm = TempRecurringTransactionForm.Empty }

            return model
        }

    /// Validate a temporary recurring transaction form. Pure function — no DB access.
    static member validateTempItem
        (name : string) (amount : string) (scheduleType : string)
        (week : string) (day : string) (date : string)
        : Result<TempRecurringTransaction, string> =
        let trimmedName = name.Trim()
        if System.String.IsNullOrEmpty trimmedName then
            Error "You must enter a name for this temporary recurring transaction."
        else
            match System.Decimal.TryParse (amount : string) with
            | false, _ -> Error $"Could not parse {amount} to a number or decimal."
            | true, amt when amt < 0.01M -> Error "Amount must be greater than 0.01."
            | true, amt when amt % 0.01M <> 0M -> Error "Amount cannot have more than two decimal places."
            | true, amt ->
                match scheduleType with
                | "week" ->
                    let weekOk, weekVal = System.Int32.TryParse week
                    let dayOk, dayVal = System.Int32.TryParse day
                    if not weekOk || weekVal < 1 || weekVal > 4 then
                        Error "Week of month must be between 1 and 4."
                    elif not dayOk || dayVal < 0 || dayVal > 6 then
                        Error "Day of week must be between 0 (Sunday) and 6 (Saturday)."
                    else
                        Ok { Name = trimmedName; Amount = amt
                             Schedule = ByWeekOfMonth { WeekOfMonth = WeekOfMonth.FromInt weekVal; DayOfWeek = enum<System.DayOfWeek> dayVal } }
                | "date" ->
                    let dateOk, dateVal = System.Int32.TryParse date
                    if not dateOk || dateVal < 1 || dateVal > 31 then
                        Error "Date of month must be between 1 and 31."
                    else
                        Ok { Name = trimmedName; Amount = amt
                             Schedule = ByCalendarDate { Date = dateVal } }
                | other ->
                    Error $"Unrecognized schedule type {other}."

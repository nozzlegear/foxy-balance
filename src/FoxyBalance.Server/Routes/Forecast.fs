namespace FoxyBalance.Server.Routes
open System
open System.Text.Json
open Giraffe
open FoxyBalance.Server
open FoxyBalance.Database.Models
open FoxyBalance.Server.Models.ViewModels
open FoxyBalance.Server.Services
open Microsoft.Extensions.DependencyInjection

module Views = FoxyBalance.Server.Views.Forecast

module Forecast =

    [<CLIMutable>]
    type TempRecurringTransactionDto =
        { Name : string
          Amount : decimal
          Week : int option
          Day : int option
          Date : int option
          Type : string }   // "bill" | "income"

    let private jsonOptions = JsonSerializerOptions()

    let private deserializeTempItems (tmpJson : string) : TempRecurringTransaction list =
        if System.String.IsNullOrWhiteSpace tmpJson then []
        else
            try
                let dtos = JsonSerializer.Deserialize<TempRecurringTransactionDto list>(tmpJson, jsonOptions)
                dtos
                |> List.choose (fun d ->
                    let tempType =
                        match d.Type with
                        | "income" -> RecurringTransactionType.Income
                        | _ -> RecurringTransactionType.Bill
                    match d.Week, d.Day, d.Date with
                    | Some w, Some day, _ ->
                        Some { Name = d.Name; Amount = d.Amount
                               Schedule = ScheduleType.ByWeekOfMonth
                                   { WeekOfMonth = WeekOfMonth.FromInt w
                                     DayOfWeek = enum<System.DayOfWeek> day }
                               Type = tempType }
                    | _, _, Some date ->
                        Some { Name = d.Name; Amount = d.Amount
                               Schedule = ScheduleType.ByCalendarDate { Date = date; Apply = None }
                               Type = tempType }
                    | _ -> None)
            with
            | _ -> []

    let private serializeTempItems (items : TempRecurringTransaction list) : string =
        let dtos =
            items
            |> List.map (fun t ->
                let typeStr = match t.Type with | RecurringTransactionType.Bill -> "bill" | RecurringTransactionType.Income -> "income"
                match t.Schedule with
                | ScheduleType.ByWeekOfMonth sched ->
                    { Name = t.Name; Amount = t.Amount
                      Week = Some (sched.WeekOfMonth.ToInt())
                      Day = Some (int sched.DayOfWeek)
                      Date = None
                      Type = typeStr }
                | ScheduleType.ByCalendarDate sched ->
                    { Name = t.Name; Amount = t.Amount
                      Week = None; Day = None; Date = Some sched.Date
                      Type = typeStr })
        JsonSerializer.Serialize<TempRecurringTransactionDto list>(dtos, jsonOptions)

    let private canonicalUrl (startDate : string) (endDate : string) (toggled : bool) (txnValues : string list) (overrideBalance : string) (tmpJson : string) : string =
        let parts = System.Collections.Generic.List<string>()
        parts.Add($"startDate={Uri.EscapeDataString startDate}")
        parts.Add($"endDate={Uri.EscapeDataString endDate}")
        if toggled then
            parts.Add("toggled=1")
            // Carry the current dates as prevStartDate/prevEndDate so the next submit can
            // detect a date-range change and drop the stale txn allow-list (see forecastHandler).
            parts.Add($"prevStartDate={Uri.EscapeDataString startDate}")
            parts.Add($"prevEndDate={Uri.EscapeDataString endDate}")
            for tx in txnValues do
                parts.Add($"txn={Uri.EscapeDataString tx}")
        if not (System.String.IsNullOrEmpty overrideBalance) then
            parts.Add($"overrideBalance={Uri.EscapeDataString overrideBalance}")
        if not (System.String.IsNullOrEmpty tmpJson) then
            parts.Add($"tmp={Uri.EscapeDataString tmpJson}")
        let joined = System.String.Join("&", parts)
        $"/forecast?{joined}"

    let forecastHandler : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let forecastService = ctx.GetService<ForecastService>()
            let today = DateTimeOffset.UtcNow

            // Read query params; fill defaults when absent.
            let startDateStrOpt = ctx.TryGetQueryStringValue "startDate"
            let endDateStrOpt = ctx.TryGetQueryStringValue "endDate"

            let startDateStr, endDateStr =
                match startDateStrOpt, endDateStrOpt with
                | Some s, Some e -> s, e
                | _ ->
                    // Default: today → today + 30 days
                    let s = Format.date today
                    let e = Format.date (today.AddDays(30.0))
                    s, e

            match ForecastService.validate startDateStr endDateStr today with
            | Error msg ->
                // Render error view with raw date strings preserved, no chart/table.
                let model : ForecastViewModel =
                    { Error = Some msg
                      StartDateStr = startDateStr
                      EndDateStr = endDateStr
                      StartDate = today
                      EndDate = today.AddDays(30.0)
                      StartingBalance = 0M
                      OverrideStartingBalanceStr = ""
                      Rows = []
                      ChartDates = []
                      ChartBalances = []
                      TmpJson = ""
                      TempItems = []
                      TempItemForm = TempRecurringTransactionForm.Empty }
                return! (setStatusCode 422 >=> htmlView (Views.page model)) next ctx
            | Ok parsed ->
                // Repeated query params: txn (toggled-on event ids).
                let txnValues =
                    ctx.Request.Query.["txn"]
                    |> Seq.toList
                    |> List.filter (System.String.IsNullOrWhiteSpace >> not)

                let hasToggled = ctx.TryGetQueryStringValue "toggled" |> Option.isSome

                // Deserialize tmp param.
                let tmpJson = ctx.TryGetQueryStringValue "tmp" |> Option.defaultValue ""
                let tmpItems = deserializeTempItems tmpJson

                // If the date range changed since the previous render, the carried txn
                // allow-list is stale (its ids refer to events from the old range). Drop it
                // and render all events on. Same range → honor the user's toggles.
                let datesChanged =
                    ForecastService.datesChangedSince
                        hasToggled
                        (ctx.TryGetQueryStringValue "prevStartDate")
                        (ctx.TryGetQueryStringValue "prevEndDate")
                        startDateStr
                        endDateStr

                let includedEventIds =
                    if hasToggled && not datesChanged then Some (Set.ofList txnValues) else None

                // Starting-balance override: parse the raw string into a decimal option.
                // Empty/absent → None (use DB-computed balance). Invalid → None (silently ignored).
                let overrideBalanceStr = ctx.TryGetQueryStringValue "overrideBalance" |> Option.defaultValue ""
                let overrideBalance =
                    if System.String.IsNullOrWhiteSpace overrideBalanceStr then None
                    else
                        match System.Decimal.TryParse overrideBalanceStr with
                        | true, v -> Some v
                        | false, _ -> None

                // Remove temp item?
                match ctx.TryGetQueryStringValue "removeTmp" with
                | Some removeIdxStr ->
                    match System.Int32.TryParse removeIdxStr with
                    | true, idx when idx >= 0 && idx < List.length tmpItems ->
                        let updated = List.removeAt idx tmpItems
                        let newTmpJson = serializeTempItems updated
                        let url = canonicalUrl startDateStr endDateStr true txnValues overrideBalanceStr newTmpJson
                        return! redirectTo false url next ctx
                    | _ ->
                        let url = canonicalUrl startDateStr endDateStr true txnValues overrideBalanceStr (serializeTempItems tmpItems)
                        return! redirectTo false url next ctx
                | None ->
                    // Add temp item?
                    let tmpName = ctx.TryGetQueryStringValue "tmpName" |> Option.defaultValue ""
                    if not (System.String.IsNullOrWhiteSpace tmpName) then
                        let tmpAmount = ctx.TryGetQueryStringValue "tmpAmount" |> Option.defaultValue ""
                        let tmpScheduleType = ctx.TryGetQueryStringValue "tmpScheduleType" |> Option.defaultValue "week"
                        let tmpWeek = ctx.TryGetQueryStringValue "tmpWeek" |> Option.defaultValue "1"
                        let tmpDay = ctx.TryGetQueryStringValue "tmpDay" |> Option.defaultValue "0"
                        let tmpDate = ctx.TryGetQueryStringValue "tmpDate" |> Option.defaultValue "1"
                        let tmpType = ctx.TryGetQueryStringValue "tmpType" |> Option.defaultValue "bill"

                        match ForecastService.validateTempItem tmpName tmpAmount tmpScheduleType tmpWeek tmpDay tmpDate tmpType with
                        | Ok item ->
                            let updated = tmpItems @ [item]
                            let newTmpJson = serializeTempItems updated
                            let url = canonicalUrl startDateStr endDateStr true txnValues overrideBalanceStr newTmpJson
                            return! redirectTo false url next ctx
                        | Error errMsg ->
                            // Build forecast model preserving toggles + tmp, then decorate with form error.
                            let! model =
                                forecastService.BuildForecastAsync(
                                    session.UserId, parsed.StartDate, parsed.EndDate,
                                    ?includedEventIds = includedEventIds,
                                    ?overrideStartingBalance = overrideBalance,
                                    temporaryRecurringTransactions = tmpItems)
                            let decoratedForm : TempRecurringTransactionForm =
                                { Error = Some errMsg
                                  Name = tmpName
                                  Amount = tmpAmount
                                  ScheduleType = tmpScheduleType
                                  Week = tmpWeek
                                  Day = tmpDay
                                  Date = tmpDate
                                  Type = tmpType }
                            let decoratedModel =
                                { model with
                                    TmpJson = serializeTempItems tmpItems
                                    TempItems = tmpItems
                                    OverrideStartingBalanceStr = overrideBalanceStr
                                    TempItemForm = decoratedForm }
                            return! (setStatusCode 422 >=> htmlView (Views.page decoratedModel)) next ctx
                    else
                        // Normal forecast render.
                        let! model =
                            forecastService.BuildForecastAsync(
                                session.UserId, parsed.StartDate, parsed.EndDate,
                                ?includedEventIds = includedEventIds,
                                ?overrideStartingBalance = overrideBalance,
                                temporaryRecurringTransactions = tmpItems)
                        let decoratedModel =
                            { model with
                                TmpJson = serializeTempItems tmpItems
                                TempItems = tmpItems
                                OverrideStartingBalanceStr = overrideBalanceStr }
                        return! htmlView (Views.page decoratedModel) next ctx
        })


namespace FoxyBalance.Server.Views

open FoxyBalance.Database.Models
open FoxyBalance.Server.Models.ViewModels
open FoxyBalance.Server.Views.Components
open Giraffe.ViewEngine
module G = HtmlElements
module A = Attributes

module Forecast =

    let private amountCell (amount : decimal) : XmlNode =
        if amount >= 0M then
            span [_class "amount credit"] [
                Format.amountWithPositiveSign amount |> str
            ]
        else
            span [_class "amount debit"] [
                Format.amountWithNegativeSign (abs amount) |> str
            ]

    let private formatWeekOfMonth = function
        | FirstWeek -> "1st"
        | SecondWeek -> "2nd"
        | ThirdWeek -> "3rd"
        | FourthWeek -> "4th"

    let private formatDayOfWeek = function
        | System.DayOfWeek.Sunday -> "Sunday"
        | System.DayOfWeek.Monday -> "Monday"
        | System.DayOfWeek.Tuesday -> "Tuesday"
        | System.DayOfWeek.Wednesday -> "Wednesday"
        | System.DayOfWeek.Thursday -> "Thursday"
        | System.DayOfWeek.Friday -> "Friday"
        | System.DayOfWeek.Saturday -> "Saturday"
        | _ -> failwith "Unexpected DayOfWeek value"

    let private scheduleLabel (schedule : ScheduleType) : string =
        match schedule with
        | ScheduleType.ByWeekOfMonth sched ->
            $"{formatWeekOfMonth sched.WeekOfMonth} week, {formatDayOfWeek sched.DayOfWeek}"
        | ScheduleType.ByCalendarDate sched ->
            $"Day {sched.Date} of month"

    /// Hidden inputs that carry forecast state (dates + toggled + tmp + on-event txn ids)
    /// across GET forms that live outside the main forecast form (temp list + add dialog).
    /// prevStartDate/prevEndDate mirror the current dates so the next submit can detect a
    /// date-range change and drop the stale txn allow-list (see forecastHandler).
    let private forecastStateHiddenInputs (model : ForecastViewModel) : XmlNode list =
        [
            G.input [A._type "hidden"; A._name "startDate"; A._value model.StartDateStr]
            G.input [A._type "hidden"; A._name "endDate"; A._value model.EndDateStr]
            G.input [A._type "hidden"; A._name "toggled"; A._value "1"]
            G.input [A._type "hidden"; A._name "prevStartDate"; A._value model.StartDateStr]
            G.input [A._type "hidden"; A._name "prevEndDate"; A._value model.EndDateStr]
            if not (System.String.IsNullOrEmpty model.TmpJson) then
                G.input [A._type "hidden"; A._name "tmp"; A._value model.TmpJson]
            for row in model.Rows do
                if row.EventId.IsSome && not row.IsTemporary && not row.IsToggledOff then
                    G.input [A._type "hidden"; A._name "txn"; A._value row.EventId.Value]
        ]

    /// Canonical forecast URL for the current state (dates + toggled on-events + tmp), used by
    /// the dialog's Cancel link so dismissing it never mutates the forecast.
    let private canonicalForecastUrl (model : ForecastViewModel) : string =
        let parts = System.Collections.Generic.List<string>()
        parts.Add("startDate=" + System.Uri.EscapeDataString model.StartDateStr)
        parts.Add("endDate=" + System.Uri.EscapeDataString model.EndDateStr)
        parts.Add("toggled=1")
        parts.Add("prevStartDate=" + System.Uri.EscapeDataString model.StartDateStr)
        parts.Add("prevEndDate=" + System.Uri.EscapeDataString model.EndDateStr)
        for row in model.Rows do
            if row.EventId.IsSome && not row.IsTemporary && not row.IsToggledOff then
                parts.Add("txn=" + System.Uri.EscapeDataString row.EventId.Value)
        if not (System.String.IsNullOrEmpty model.TmpJson) then
            parts.Add("tmp=" + System.Uri.EscapeDataString model.TmpJson)
        "/forecast?" + System.String.Join("&", parts)

    /// A labelled date input that expands to fill the available row width.
    let private dateField (label : string) (htmlName : string) (value : string) : XmlNode =
        G.div [A._class "control is-expanded"] [
            G.label [A._class "label"; A._for htmlName] [G.str label]
            G.input [
                A._type "date"
                A._class "input"
                A._name htmlName
                A._id htmlName
                A._value value
                A._required
            ]
        ]

    /// Start date, end date, and the Forecast submit button sharing a single bottom-aligned row.
    /// Bulma's `field is-grouped` lays them out in a row and `control is-expanded` makes the
    /// date fields grow to fill the container (the button keeps its natural width).
    let private controlsRow (model : ForecastViewModel) : XmlNode =
        G.div [A._class "field is-grouped forecast-controls"] [
            dateField "Start date" "startDate" model.StartDateStr
            dateField "End date" "endDate" model.EndDateStr
            G.div [A._class "control"] [
                G.button [A._type "submit"; A._class "button is-success"] [G.str "Forecast"]
            ]
        ]

    /// Native <dialog> holding the add-temporary form. Hidden by default and opened by the
    /// table's "Add Temporary Transaction" button via the popover API (no JavaScript).
    /// When a validation error exists it renders open (non-popover) so the error is visible.
    let private addTempDialog (model : ForecastViewModel) : XmlNode =
        let dialogAttrs =
            [ A._id "forecast-temp-dialog" ]
            @ (if model.TempItemForm.Error.IsSome then
                   [ G.attr "open" "" ]
               else
                   [ G.attr "popover" "auto" ])

        G.dialog dialogAttrs [
            Form.create [Form.Method Form.Get; Form.Action "/forecast"] [
                Form.Element.Raw (forecastStateHiddenInputs model)

                Form.Element.Raw [
                    G.h2 [A._class "title is-5"] [G.str "Add temporary recurring transaction"]
                ]

                Form.Element.TextInput [
                    Form.LabelText "Name"
                    Form.HtmlName "tmpName"
                    Form.Value model.TempItemForm.Name ]

                Form.Element.NumberInput [
                    Form.LabelText "Amount"
                    Form.HtmlName "tmpAmount"
                    Form.Min 0.01M
                    Form.Step 0.01M
                    Form.Value model.TempItemForm.Amount ]

                Form.Element.SelectBox [
                    Form.SelectOption.LabelText "Type"
                    Form.SelectOption.HtmlName "tmpType"
                    Form.SelectOption.Value model.TempItemForm.Type
                    Form.SelectOption.Options [
                        {| Label = "Bill"; Value = "bill"; Selected = model.TempItemForm.Type = "bill" |}
                        {| Label = "Income"; Value = "income"; Selected = model.TempItemForm.Type = "income" |}
                    ] ]

                Form.Element.SelectBox [
                    Form.SelectOption.LabelText "Schedule type"
                    Form.SelectOption.HtmlName "tmpScheduleType"
                    Form.SelectOption.Value model.TempItemForm.ScheduleType
                    Form.SelectOption.Options [
                        {| Label = "By week of month"; Value = "week"; Selected = model.TempItemForm.ScheduleType = "week" |}
                        {| Label = "By calendar date"; Value = "date"; Selected = model.TempItemForm.ScheduleType = "date" |}
                    ] ]

                Form.Element.SelectBox [
                    Form.SelectOption.LabelText "Week of month"
                    Form.SelectOption.HtmlName "tmpWeek"
                    Form.SelectOption.Value model.TempItemForm.Week
                    Form.SelectOption.Options [
                        {| Label = "1st week"; Value = "1"; Selected = model.TempItemForm.Week = "1" |}
                        {| Label = "2nd week"; Value = "2"; Selected = model.TempItemForm.Week = "2" |}
                        {| Label = "3rd week"; Value = "3"; Selected = model.TempItemForm.Week = "3" |}
                        {| Label = "4th week"; Value = "4"; Selected = model.TempItemForm.Week = "4" |}
                    ] ]

                Form.Element.SelectBox [
                    Form.SelectOption.LabelText "Day of week"
                    Form.SelectOption.HtmlName "tmpDay"
                    Form.SelectOption.Value model.TempItemForm.Day
                    Form.SelectOption.Options [
                        {| Label = "Sunday"; Value = "0"; Selected = model.TempItemForm.Day = "0" |}
                        {| Label = "Monday"; Value = "1"; Selected = model.TempItemForm.Day = "1" |}
                        {| Label = "Tuesday"; Value = "2"; Selected = model.TempItemForm.Day = "2" |}
                        {| Label = "Wednesday"; Value = "3"; Selected = model.TempItemForm.Day = "3" |}
                        {| Label = "Thursday"; Value = "4"; Selected = model.TempItemForm.Day = "4" |}
                        {| Label = "Friday"; Value = "5"; Selected = model.TempItemForm.Day = "5" |}
                        {| Label = "Saturday"; Value = "6"; Selected = model.TempItemForm.Day = "6" |}
                    ] ]

                Form.Element.NumberInput [
                    Form.LabelText "Date of month"
                    Form.HtmlName "tmpDate"
                    Form.Min 1M
                    Form.Max 31M
                    Form.Value model.TempItemForm.Date ]

                Form.Element.MaybeError model.TempItemForm.Error

                Form.Element.Raw [
                    G.div [A._class "field is-grouped"] [
                        G.div [A._class "control"] [
                            G.button [A._type "submit"; A._class "button is-success"] [
                                G.str "Add temporary transaction"
                            ]
                        ]
                        G.div [A._class "control"] [
                            G.a [A._class "button is-light"; A._href (canonicalForecastUrl model)] [
                                G.str "Cancel"
                            ]
                        ]
                    ]
                ]
            ]
        ]

    let page (model : ForecastViewModel) : XmlNode =
        let title = "Forecast"

        // Compute income/bills subtotals from event rows (exclude status rows + toggled-off rows).
        let incomeTotal, billsTotal =
            model.Rows
            |> List.filter (fun r -> r.Amount.IsSome && not r.IsToggledOff)
            |> List.choose (fun r -> r.Amount)
            |> List.fold (fun (income, bills) amt ->
                if amt >= 0M then (income + amt, bills)
                else (income, bills + abs amt)
            ) (0M, 0M)

        let projectedBalance =
            if List.isEmpty model.Rows then 0M
            else (List.last model.Rows).RunningBalance

        let body =
            if model.Error.IsSome then
                [
                    // Date form is still shown so the user can fix the dates
                    Shared.level [
                        Shared.LeftLevel [
                            Shared.LevelItem.Element (Shared.title title)
                        ]
                        Shared.RightLevel []
                    ]

                    Form.create [Form.Method Form.Get; Form.Action "/forecast"] [
                        Form.Element.Raw [ controlsRow model ]

                        Form.Element.MaybeError model.Error ]
                ]
            else
                // Build JSON data for ECharts: numbers + yyyy-MM-dd strings only.
                let datesJson =
                    model.ChartDates
                    |> List.map (sprintf "\"%s\"")
                    |> String.concat ","
                let balancesJson =
                    model.ChartBalances
                    |> List.map (string)
                    |> String.concat ","
                let chartJson = sprintf "{\"dates\":[%s],\"balances\":[%s]}" datesJson balancesJson

                [
                    // Level 1: Summary stats
                    Shared.evenlySpacedLevel [
                        Shared.LevelItem.HeadingAndTitle ("Starting balance", Format.amountWithDollarSign model.StartingBalance)
                        Shared.LevelItem.HeadingAndTitle ("Income (in range)", Format.amountWithDollarSign incomeTotal)
                        Shared.LevelItem.HeadingAndTitle ("Bills (in range)", Format.amountWithDollarSign billsTotal)
                        Shared.LevelItem.HeadingAndTitle (sprintf "Projected %s" (Format.date model.EndDate), Format.amountWithDollarSign projectedBalance)
                    ]

                    // Main form wraps chart + controls + table so the Forecast button submits
                    // the date range AND the checkbox toggles together (no JavaScript).
                    Form.create [Form.Method Form.Get; Form.Action "/forecast"] [
                        // Hidden state carriers (dates come from the visible inputs; txn from the checkboxes).
                        // prevStartDate/prevEndDate mirror the current render's dates so the next
                        // submit can detect a date-range change and drop the stale txn allow-list.
                        Form.Element.Raw [
                            G.input [A._type "hidden"; A._name "toggled"; A._value "1"]
                            G.input [A._type "hidden"; A._name "prevStartDate"; A._value model.StartDateStr]
                            G.input [A._type "hidden"; A._name "prevEndDate"; A._value model.EndDateStr]
                            if not (System.String.IsNullOrEmpty model.TmpJson) then
                                G.input [A._type "hidden"; A._name "tmp"; A._value model.TmpJson]
                        ]

                        // Chart
                        Form.Element.Raw [
                            div [_id "forecast-chart"; _style "height:400px"] []

                            // ECharts from CDN (forecast page only)
                            script [_type "text/javascript"; _src "https://cdn.jsdelivr.net/npm/echarts@5.5.1/dist/echarts.min.js"] []

                            // Chart data as JSON
                            script [_type "application/json"; _id "forecast-data"] [
                                G.rawText chartJson
                            ]

                            // Chart init script
                            script [_type "text/javascript"] [
                                G.rawText """
document.addEventListener('DOMContentLoaded', function () {
    var dataEl = document.getElementById('forecast-data');
    var data = JSON.parse(dataEl.textContent);
    var chart = echarts.init(document.getElementById('forecast-chart'));
    chart.setOption({
        tooltip: { trigger: 'axis' },
        xAxis: {
            type: 'category',
            data: data.dates,
            axisLabel: { rotate: 30 }
        },
        yAxis: { type: 'value' },
        series: [{
            type: 'line',
            smooth: true,
            data: data.balances,
            areaStyle: {},
            markLine: {
                data: [{ yAxis: 0 }]
            }
        }]
    });
});
"""
                            ]
                        ]

                        // Date range + Forecast button, directly under the chart and above the table
                        Form.Element.Raw [ controlsRow model ]

                        // Table
                        Form.Element.Raw [
                            Shared.table [
                                Shared.TableHead [
                                    Shared.TableCell (str "In forecast")
                                    Shared.TableCell (str "Date")
                                    Shared.TableCell (str "Type")
                                    Shared.TableCell (str "Description")
                                    Shared.TableCell (str "Transaction Amount")
                                    Shared.TableCell (str "Balance")
                                ]
                                Shared.TableBody [
                                    for row in model.Rows do
                                        yield Shared.TableRow [
                                            // Checkbox cell: only for toggleable event rows (not temp, not status rows)
                                            Shared.TableCell (
                                                match row.EventId with
                                                | Some eventId when not row.IsTemporary ->
                                                    let checkboxAttrs =
                                                        [ A._type "checkbox"
                                                          A._name "txn"
                                                          A._value eventId
                                                          A._class "forecast-toggle"
                                                          G.attr "aria-label" (sprintf "Include %s in forecast" row.Description) ]
                                                        @ (if not row.IsToggledOff then [A._checked] else [])
                                                    G.input checkboxAttrs
                                                | _ -> str ""
                                            )
                                            Shared.TableCell (Format.date row.Date |> str)
                                            Shared.TableCell (
                                                if row.IsTemporary then
                                                    G.span [] [
                                                        str row.Label
                                                        str " "
                                                        G.span [A._class "tag is-warning is-light"] [str "Temporary"]
                                                    ]
                                                else
                                                    str row.Label
                                            )
                                            Shared.TableCell (str row.Description)
                                            Shared.TableCell (
                                                match row.Amount with
                                                | Some a -> amountCell a
                                                | None -> str ""
                                            )
                                            Shared.TableCell (Format.amountWithDollarSign row.RunningBalance |> str)
                                        ]
                                ]
                            ]
                        ]

                        // Trigger for the add-temporary dialog, at the bottom of the table.
                        Form.Element.Raw [
                            G.div [A._class "field"] [
                                G.div [A._class "control"] [
                                    G.button [
                                        A._type "button"
                                        A._class "button is-link"
                                        G.attr "popovertarget" "forecast-temp-dialog"
                                    ] [G.str "Add Temporary Transaction"]
                                ]
                            ]
                        ]
                    ]

                    // Existing temporary recurring transactions (only when any exist)
                    if not (List.isEmpty model.TempItems) then
                        G.div [A._class "box"] [
                            G.p [A._class "title is-5"] [G.str "Temporary recurring transactions"]
                            G.p [A._class "is-size-7 has-text-grey"] [
                                G.str "These only affect this forecast and are not saved to the database."
                            ]

                            for (i, item) in List.indexed model.TempItems do
                                G.div [A._class "columns is-vcentered"] [
                                    G.div [A._class "column"] [
                                        G.p [A._class "has-text-weight-bold"] [G.str item.Name]
                                        G.p [A._class "is-size-7"] [
                                            G.str (sprintf "%s — %s"
                                                (Format.amountWithDollarSign item.Amount)
                                                (scheduleLabel item.Schedule))
                                        ]
                                    ]
                                    G.div [A._class "column is-narrow"] [
                                        G.form [A._method "GET"; A._action "/forecast"] [
                                            yield! forecastStateHiddenInputs model
                                            G.input [A._type "hidden"; A._name "removeTmp"; A._value (string i)]
                                            G.button [
                                                A._type "submit"
                                                A._class "delete is-small"
                                                G.attr "aria-label" (sprintf "Remove %s" item.Name)
                                            ] []
                                        ]
                                    ]
                                ]
                        ]

                    // Add form (hidden until the trigger button opens it)
                    addTempDialog model
                ]

        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection body

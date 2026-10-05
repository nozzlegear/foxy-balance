namespace FoxyBalance.Server.Views

open FoxyBalance.Database.Models
open FoxyBalance.Server.Views.Components
open FoxyBalance.Server.Models.ViewModels
open Giraffe.ViewEngine
module G = HtmlElements
module A = Attributes

module Bills =
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

    /// Human-readable description of a schedule for the list table.
    /// ByWeekOfMonth: "1st week, Monday". ByCalendarDate: "Day 15 of month".
    let private scheduleLabel (schedule : ScheduleType) =
        match schedule with
        | ScheduleType.ByWeekOfMonth sched ->
            $"{formatWeekOfMonth sched.WeekOfMonth} week, {formatDayOfWeek sched.DayOfWeek}"
        | ScheduleType.ByCalendarDate sched ->
            $"Day {sched.Date} of month"

    let private recurringTypeLabel (t : RecurringTransactionType) =
        match t with
        | RecurringTransactionType.Bill -> "Bill"
        | RecurringTransactionType.Income -> "Income"


    let listBillsPage (model : RecurringBillsListViewModel) : XmlNode =
        let title = "Recurring"

        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (Shared.title title)
                ]
                Shared.RightLevel [
                    Shared.LevelItem.Element (G.a [A._href "/recurring/match"; A._class "button is-info"] [G.str "Match Transactions"])
                    Shared.LevelItem.Element (G.a [A._href "/recurring/new"; A._class "button is-success"] [G.str "New Recurring"])
                ]
            ]

            if Seq.isEmpty model.Bills then
                G.p [] [G.str "No recurring transactions found. Create one to get started!"]
            else
                Shared.table [
                    Shared.TableHead [
                        Shared.TableCell (G.str "Name")
                        Shared.TableCell (G.str "Type")
                        Shared.TableCell (G.str "Amount")
                        Shared.TableCell (G.str "Schedule")
                        Shared.TableCell (G.str "Last Applied")
                        Shared.TableCell (G.str "Status")
                        Shared.TableCell (G.str "Actions")
                    ]
                    Shared.TableBody [
                        for bill in model.Bills do
                            yield Shared.TableRow [
                                Shared.TableCell (G.a [A._href (sprintf "/recurring/%i" bill.Id)] [G.str bill.Name])
                                Shared.TableCell (G.str (recurringTypeLabel bill.Type))
                                Shared.TableCell (Format.amountWithDollarSign bill.Amount |> G.str)
                                Shared.TableCell (G.str $"{scheduleTypeLabel bill.Schedule} week, {formatDayOfWeek (scheduleDayOfWeek bill.Schedule)}")
                                Shared.TableCell (
                                    match bill.LastAppliedDate with
                                    | Some date -> Format.date date |> G.str
                                    | None -> G.str "Never"
                                )
                                Shared.TableCell (G.str (if bill.Active then "Active" else "Paused"))
                                Shared.TableCell (
                                    Form.create [Form.Method Form.Post; Form.Action (sprintf "/recurring/%i/toggle" bill.Id)] [
                                        Form.Element.Button [
                                            Form.ButtonText (if bill.Active then "Pause" else "Resume")
                                            Form.Color (if bill.Active then Form.ButtonColor.Warning else Form.ButtonColor.Success)
                                            Form.Type Form.Submit
                                        ]
                                    ]
                                )
                            ]
                    ]
                ]
        ]

    let createOrEditBillPage (model : BillViewModel) : XmlNode =
        let (isNew, billId, viewModel) =
            match model with
            | NewBill vm -> (true, 0L, vm)
            | ExistingBill (id, vm) -> (false, id, vm)

        let title = if isNew then "New Recurring Transaction" else "Edit Recurring Transaction"
        let action = if isNew then "/recurring/new" else sprintf "/recurring/%i" billId
        let buttonText = if isNew then "Create Recurring Transaction" else "Update Recurring Transaction"

        let deleteButton =
            if isNew then
                None
            else
                Some (
                    Form.Element.Button [
                        Form.ButtonText "Delete"
                        Form.ButtonFormAction (sprintf "/recurring/%i/delete" billId)
                        Form.Color Form.ButtonColor.Danger
                        Form.OnClick "return confirm('Are you sure you want to delete this recurring transaction? This action cannot be undone.')"
                        Form.Type Form.Submit
                    ]
                )

        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (Shared.title title)
                ]
                Shared.RightLevel [
                     G.a [A._href "/recurring"; A._class "button"] [G.str "Cancel"]
                     |> Shared.LevelItem.Element
                ]
            ]
            Form.create [Form.Method Form.Post; Form.Action action; Form.AutoComplete false] [
                Form.Element.TextInput [
                    Form.Placeholder "Monthly utility bill"
                    Form.LabelText "Name or description"
                    Form.HtmlName "name"
                    Form.Required
                    Form.Value viewModel.Name ]

                Form.Element.NumberInput [
                    Form.Placeholder "100.00"
                    Form.LabelText "Amount"
                    Form.HtmlName "amount"
                    Form.Min 0.01M
                    Form.Step 0.01M
                    Form.Required
                    Form.Value viewModel.Amount ]

                Form.Element.SelectBox [
                    Form.SelectOption.LabelText "Type"
                    Form.SelectOption.HtmlName "type"
                    Form.SelectOption.Options [
                        {| Label = "Bill"; Value = "bill"; Selected = viewModel.Type = "bill" |}
                        {| Label = "Income"; Value = "income"; Selected = viewModel.Type = "income" |}
                    ]
                    Form.SelectOption.Value viewModel.Type ]

                Form.Element.SelectBox [
                    Form.SelectOption.LabelText "Schedule type"
                    Form.SelectOption.HtmlName "scheduleType"
                    Form.SelectOption.Options [
                        {| Label = "By week of month"; Value = "week"; Selected = viewModel.ScheduleType = "week" |}
                        {| Label = "By calendar date"; Value = "date"; Selected = viewModel.ScheduleType = "date" |}
                    ]
                    Form.SelectOption.Value viewModel.ScheduleType ]

                // Week-of-month fields: only relevant (and rendered) for the "week" schedule type.
                Form.Element.MaybeElement (
                    if viewModel.ScheduleType = "week" then
                        Some (Form.Element.Group [
                            Form.Element.SelectBox [
                                Form.SelectOption.LabelText "Week of Month"
                                Form.SelectOption.HtmlName "weekOfMonth"
                                Form.SelectOption.Options [
                                    {| Label = "1st week"; Value = "1"; Selected = viewModel.WeekOfMonth = "1" |}
                                    {| Label = "2nd week"; Value = "2"; Selected = viewModel.WeekOfMonth = "2" |}
                                    {| Label = "3rd week"; Value = "3"; Selected = viewModel.WeekOfMonth = "3" |}
                                    {| Label = "4th week"; Value = "4"; Selected = viewModel.WeekOfMonth = "4" |}
                                ]
                                Form.SelectOption.Value viewModel.WeekOfMonth ]

                            Form.Element.SelectBox [
                                Form.SelectOption.LabelText "Day of Week"
                                Form.SelectOption.HtmlName "dayOfWeek"
                                Form.SelectOption.Options [
                                    {| Label = "Sunday"; Value = "0"; Selected = viewModel.DayOfWeek = "0" |}
                                    {| Label = "Monday"; Value = "1"; Selected = viewModel.DayOfWeek = "1" |}
                                    {| Label = "Tuesday"; Value = "2"; Selected = viewModel.DayOfWeek = "2" |}
                                    {| Label = "Wednesday"; Value = "3"; Selected = viewModel.DayOfWeek = "3" |}
                                    {| Label = "Thursday"; Value = "4"; Selected = viewModel.DayOfWeek = "4" |}
                                    {| Label = "Friday"; Value = "5"; Selected = viewModel.DayOfWeek = "5" |}
                                    {| Label = "Saturday"; Value = "6"; Selected = viewModel.DayOfWeek = "6" |}
                                ]
                                Form.SelectOption.Value viewModel.DayOfWeek ]
                        ])
                    else
                        None)

                // Calendar-date fields: only relevant (and rendered) for the "date" schedule type.
                Form.Element.MaybeElement (
                    if viewModel.ScheduleType = "date" then
                        // Determine whether the chosen day may not exist in every month (29-31)
                        // and whether the user has already chosen an apply policy.
                        let dayOpt =
                            match System.Int32.TryParse viewModel.DayOfMonth with
                            | true, d -> Some d
                            | false, _ -> None
                        let dayIsPotentiallyInvalid =
                            dayOpt |> Option.exists (fun d -> d >= 29)
                        let applyNotChosen = System.String.IsNullOrEmpty viewModel.ApplyDate

                        let dayOfMonthSelect =
                            Form.Element.SelectBox [
                                Form.SelectOption.LabelText "Day of Month"
                                Form.SelectOption.HtmlName "dayOfMonth"
                                Form.SelectOption.Options [
                                    for d in 1..31 do
                                        yield {| Label = string d; Value = string d; Selected = viewModel.DayOfMonth = string d |}
                                ]
                                Form.SelectOption.Value viewModel.DayOfMonth ]

                        // The "How to apply" selector only appears after a re-render triggered by
                        // submitting a potentially-invalid day without an apply policy. It stays
                        // visible once the user has chosen a policy so they can change it.
                        let applySelect =
                            Form.Element.MaybeElement (
                                if dayIsPotentiallyInvalid then
                                    Some (
                                        Form.Element.SelectBox [
                                            Form.SelectOption.LabelText "How to apply this date"
                                            Form.SelectOption.HtmlName "applyDate"
                                            Form.SelectOption.Options [
                                                {| Label = "Choose…"; Value = ""; Selected = applyNotChosen |}
                                                {| Label = "Apply early (last day of the month)"; Value = "early"; Selected = viewModel.ApplyDate = "early" |}
                                                {| Label = "Apply late (1st of next month)"; Value = "late"; Selected = viewModel.ApplyDate = "late" |}
                                            ]
                                            Form.SelectOption.Value viewModel.ApplyDate ]
                                    )
                                else
                                    None)

                        Some (Form.Element.Group [ dayOfMonthSelect; applySelect ])
                    else
                        None)

                Form.Element.MaybeError viewModel.Error

                Form.Element.Group [
                    deleteButton
                    |> Option.defaultValue Form.Element.EmptyDiv

                    Form.Element.Button [
                        Form.ButtonText buttonText
                        Form.Alignment Form.ButtonAlignment.Right
                        Form.Color Form.ButtonColor.Success
                        Form.Type Form.Submit ]
                ]
            ]
        ]

    let matchingPage (model : BillMatchingViewModel) : XmlNode =
        let title = "Match Transactions to Recurring Transactions"

        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (Shared.title title)
                ]
                Shared.RightLevel [
                     G.a [A._href "/recurring"; A._class "button"] [G.str "Back to Recurring"]
                     |> Shared.LevelItem.Element
                ]
            ]

            G.div [A._class "content"] [
                G.p [] [G.str "Below are suggested matches between your imported transactions and recurring transactions. Click \"Match\" to link them together."]
            ]

            if List.isEmpty model.MatchCandidates then
                G.p [A._class "has-text-centered has-text-grey"] [G.str "No matching suggestions found. All transactions may already be matched, or there are no unmatched imported transactions."]
            else
                for candidate in model.MatchCandidates do
                    let transaction = candidate.Transaction
                    let rt = candidate.RecurringTransaction

                    G.div [A._class "box"] [
                        G.div [A._class "columns is-vcentered"] [
                            G.div [A._class "column is-5"] [
                                G.p [A._class "has-text-weight-bold"] [G.str "Transaction"]
                                G.p [] [G.str transaction.Name]
                                G.p [A._class "is-size-7"] [
                                    G.str (sprintf "%s - %s" (Format.amountWithDollarSign transaction.Amount) (Format.date transaction.DateCreated))
                                ]
                            ]
                            G.div [A._class "column is-1 has-text-centered"] [
                                G.span [A._class "icon is-large"] [
                                    G.i [A._class "fas fa-arrow-right"] []
                                ]
                            ]
                            G.div [A._class "column is-4"] [
                                G.p [A._class "has-text-weight-bold"] [G.str "Recurring Txn"]
                                G.p [] [G.str rt.Name]
                                G.p [A._class "is-size-7"] [
                                    G.str (sprintf "%s - %s"
                                        (Format.amountWithDollarSign rt.Amount)
                                        (scheduleLabel rt.Schedule))
                                ]
                            ]
                            G.div [A._class "column is-2"] [
                                G.p [A._class "has-text-weight-bold"] [G.str (sprintf "Score: %.0f%%" candidate.MatchScore)]
                                G.form [A._method "post"; A._action (sprintf "/balance/%i/match" transaction.Id)] [
                                    G.input [A._type "hidden"; A._name "recurringTransactionId"; A._value (string rt.Id)]
                                    G.button [A._type "submit"; A._class "button is-primary"] [G.str "Match"]
                                ]
                            ]
                        ]
                    ]
        ]

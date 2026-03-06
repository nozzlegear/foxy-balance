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
        | _ -> "Unknown"

    let private daySuffix day =
        match day with
        | 1 | 21 | 31 -> "st"
        | 2 | 22 -> "nd"
        | 3 | 23 -> "rd"
        | _ -> "th"

    let listBillsPage (model : RecurringBillsListViewModel) : XmlNode =
        let title = "Recurring Bills"

        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (Shared.title title)
                ]
                Shared.RightLevel [
                    Shared.LevelItem.Element (G.a [A._href "/bills/match"; A._class "button is-info"] [G.str "Match Transactions"])
                    Shared.LevelItem.Element (G.a [A._href "/bills/new"; A._class "button is-success"] [G.str "New Bill"])
                ]
            ]

            if Seq.isEmpty model.Bills then
                G.p [] [G.str "No recurring bills found. Create one to get started!"]
            else
                Shared.table [
                    Shared.TableHead [
                        Shared.TableCell (G.str "Name")
                        Shared.TableCell (G.str "Amount")
                        Shared.TableCell (G.str "Schedule")
                        Shared.TableCell (G.str "Last Applied")
                        Shared.TableCell (G.str "Status")
                        Shared.TableCell (G.str "Actions")
                    ]
                    Shared.TableBody [
                        for bill in model.Bills do
                            let scheduleText =
                                match bill.Schedule with
                                | WeekBased(week, day) -> $"{formatWeekOfMonth week} week, {formatDayOfWeek day}"
                                | DateBased(dayOfMonth) -> $"{dayOfMonth}{daySuffix dayOfMonth} of month"

                            yield Shared.TableRow [
                                Shared.TableCell (G.a [A._href (sprintf "/bills/%i" bill.Id)] [G.str bill.Name])
                                Shared.TableCell (Format.amountWithDollarSign bill.Amount |> G.str)
                                Shared.TableCell (G.str scheduleText)
                                Shared.TableCell (
                                    match bill.LastAppliedDate with
                                    | Some date -> Format.date date |> G.str
                                    | None -> G.str "Never"
                                )
                                Shared.TableCell (G.str (if bill.Active then "Active" else "Paused"))
                                Shared.TableCell (
                                    Form.create [Form.Method Form.Post; Form.Action (sprintf "/bills/%i/toggle" bill.Id)] [
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

        let title = if isNew then "New Recurring Bill" else "Edit Recurring Bill"
        let action = if isNew then "/bills/new" else sprintf "/bills/%i" billId
        let buttonText = if isNew then "Create Bill" else "Update Bill"

        let deleteButton =
            if isNew then
                None
            else
                Some (
                    Form.Element.Button [
                        Form.ButtonText "Delete"
                        Form.ButtonFormAction (sprintf "/bills/%i/delete" billId)
                        Form.Color Form.ButtonColor.Danger
                        Form.OnClick "return confirm('Are you sure you want to delete this bill? This action cannot be undone.')"
                        Form.Type Form.Submit
                    ]
                )

        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (Shared.title title)
                ]
                Shared.RightLevel [
                     G.a [A._href "/bills"; A._class "button"] [G.str "Cancel"]
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
                    Form.SelectOption.LabelText "Schedule Type"
                    Form.SelectOption.HtmlName "scheduleType"
                    Form.SelectOption.Options [
                        {| Label = "Week of month (e.g., 2nd Wednesday)"; Value = "week"; Selected = viewModel.ScheduleType = "week" |}
                        {| Label = "Day of month (e.g., 15th of every month)"; Value = "date"; Selected = viewModel.ScheduleType = "date" |}
                    ]
                    Form.SelectOption.Value viewModel.ScheduleType ]

                Form.Element.Raw [
                    G.div [
                        A._class "week-based-fields"
                        A._style (if viewModel.ScheduleType = "week" then "" else "display:none")
                    ] [
                        G.div [A._class "field"] [
                            G.div [A._class "control"] [
                                G.label [A._class "label"; A._for "weekOfMonth"] [G.str "Week of Month"]
                                G.div [A._class "select is-fullwidth"] [
                                    G.select [A._name "weekOfMonth"; A._value viewModel.WeekOfMonth] [
                                        G.option [A._value "1"; if viewModel.WeekOfMonth = "1" then A._selected] [G.str "1st week"]
                                        G.option [A._value "2"; if viewModel.WeekOfMonth = "2" then A._selected] [G.str "2nd week"]
                                        G.option [A._value "3"; if viewModel.WeekOfMonth = "3" then A._selected] [G.str "3rd week"]
                                        G.option [A._value "4"; if viewModel.WeekOfMonth = "4" then A._selected] [G.str "4th week"]
                                    ]
                                ]
                            ]
                        ]
                        G.div [A._class "field"] [
                            G.div [A._class "control"] [
                                G.label [A._class "label"; A._for "dayOfWeek"] [G.str "Day of Week"]
                                G.div [A._class "select is-fullwidth"] [
                                    G.select [A._name "dayOfWeek"; A._value viewModel.DayOfWeek] [
                                        G.option [A._value "0"; if viewModel.DayOfWeek = "0" then A._selected] [G.str "Sunday"]
                                        G.option [A._value "1"; if viewModel.DayOfWeek = "1" then A._selected] [G.str "Monday"]
                                        G.option [A._value "2"; if viewModel.DayOfWeek = "2" then A._selected] [G.str "Tuesday"]
                                        G.option [A._value "3"; if viewModel.DayOfWeek = "3" then A._selected] [G.str "Wednesday"]
                                        G.option [A._value "4"; if viewModel.DayOfWeek = "4" then A._selected] [G.str "Thursday"]
                                        G.option [A._value "5"; if viewModel.DayOfWeek = "5" then A._selected] [G.str "Friday"]
                                        G.option [A._value "6"; if viewModel.DayOfWeek = "6" then A._selected] [G.str "Saturday"]
                                    ]
                                ]
                            ]
                        ]
                    ]

                    G.div [
                        A._class "date-based-fields"
                        A._style (if viewModel.ScheduleType = "date" then "" else "display:none")
                    ] [
                        G.div [A._class "field"] [
                            G.div [A._class "control"] [
                                G.label [A._class "label"; A._for "dayOfMonth"] [G.str "Day of Month (1-31)"]
                                G.input [A._class "input"; A._type "number"; A._name "dayOfMonth"; A._value viewModel.DayOfMonth; A._placeholder "15"; A._min "1"; A._max "31"]
                                G.p [A._class "help"] [
                                    G.str "If this day doesn't exist in a month (e.g., Feb 31st), the bill will be applied on the last day of that month."
                                ]
                            ]
                        ]
                    ]

                    G.script [] [
                        G.rawText """
                            document.addEventListener('DOMContentLoaded', function() {
                                var scheduleTypeSelect = document.querySelector('select[name="scheduleType"]');
                                var weekBasedFields = document.querySelector('.week-based-fields');
                                var dateBasedFields = document.querySelector('.date-based-fields');

                                function updateFieldVisibility() {
                                    var isWeek = scheduleTypeSelect.value === 'week';
                                    weekBasedFields.style.display = isWeek ? '' : 'none';
                                    dateBasedFields.style.display = isWeek ? 'none' : '';
                                }

                                scheduleTypeSelect.addEventListener('change', updateFieldVisibility);
                                updateFieldVisibility();
                            });
                        """
                    ]
                ]

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
        let title = "Match Transactions to Bills"

        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (Shared.title title)
                ]
                Shared.RightLevel [
                     G.a [A._href "/bills"; A._class "button"] [G.str "Back to Bills"]
                     |> Shared.LevelItem.Element
                ]
            ]

            G.div [A._class "content"] [
                G.p [] [G.str "Below are suggested matches between your imported transactions and recurring bills. Click \"Match\" to link them together."]
            ]

            if List.isEmpty model.MatchCandidates then
                G.p [A._class "has-text-centered has-text-grey"] [G.str "No matching suggestions found. All transactions may already be matched, or there are no unmatched imported transactions."]
            else
                for candidate in model.MatchCandidates do
                    let transaction = candidate.Transaction
                    let bill = candidate.RecurringBill

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
                                let scheduleDesc =
                                    match bill.Schedule with
                                    | WeekBased(week, day) -> $"{formatWeekOfMonth week} week, {formatDayOfWeek day}"
                                    | DateBased(dayOfMonth) -> $"{dayOfMonth}{daySuffix dayOfMonth} of month"

                                G.p [A._class "has-text-weight-bold"] [G.str "Bill"]
                                G.p [] [G.str bill.Name]
                                G.p [A._class "is-size-7"] [
                                    G.str $"{Format.amountWithDollarSign bill.Amount} - {scheduleDesc}"
                                ]
                            ]
                            G.div [A._class "column is-2"] [
                                G.p [A._class "has-text-weight-bold"] [G.str (sprintf "Score: %.0f%%" candidate.MatchScore)]
                                G.form [A._method "post"; A._action (sprintf "/balance/%i/match" transaction.Id)] [
                                    G.input [A._type "hidden"; A._name "billId"; A._value (string bill.Id)]
                                    G.button [A._type "submit"; A._class "button is-primary"] [G.str "Match"]
                                ]
                            ]
                        ]
                    ]
        ]

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
                            yield Shared.TableRow [
                                Shared.TableCell (G.a [A._href (sprintf "/bills/%i" bill.Id)] [G.str bill.Name])
                                Shared.TableCell (Format.amountWithDollarSign bill.Amount |> G.str)
                                Shared.TableCell (G.str $"{formatWeekOfMonth bill.WeekOfMonth} week, {formatDayOfWeek bill.DayOfWeek}")
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
                                G.p [A._class "has-text-weight-bold"] [G.str "Bill"]
                                G.p [] [G.str bill.Name]
                                G.p [A._class "is-size-7"] [
                                    G.str (sprintf "%s - %s week, %s"
                                        (Format.amountWithDollarSign bill.Amount)
                                        (formatWeekOfMonth bill.WeekOfMonth)
                                        (formatDayOfWeek bill.DayOfWeek))
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

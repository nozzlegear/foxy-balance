namespace FoxyBalance.Server.Views

open FoxyBalance.Database.Models
open FoxyBalance.Server.Views.Components
open FoxyBalance.Server.Models.ViewModels
open Giraffe.GiraffeViewEngine
module G = Giraffe.GiraffeViewEngine
module A = Giraffe.GiraffeViewEngine.Attributes

module Home =
    let homePage (model : HomePageViewModel) : XmlNode =
        let title = sprintf "Transactions - Page %i" model.Page
        let clearedStatusCell = function
            | TransactionStatus.Pending ->
                str "Pending"
            | TransactionStatus.Cleared date ->
                Format.date date
                |> str
        let typeCell = function
            | TransactionType.Check details ->
                sprintf "Check %s" details.CheckNumber
                |> str
            | TransactionType.Bill _ ->
                str "Bill"
            | TransactionType.Credit ->
                str "Credit"
            | TransactionType.Debit ->
                str "Debit"
        let amountCell transactionType amount =
            match transactionType with
            | TransactionType.Credit ->
                span [_class "amount credit"] [
                    Format.amountWithPositiveSign amount
                    |> str
                ]
            | _ ->
                span [_class "amount debit"] [
                    Format.amountWithNegativeSign amount
                    |> str
                ]
        let statusControls =
            let unselected status =
                let url =
                    Shared.statusFilterQueryParam status
                    |> sprintf "/home?status=%s" 
                a [_href url] [str (Shared.statusFilterText status)]
            let selected status =
                strong [] [str (Shared.statusFilterText status)]
            let control status =
                let el = 
                    if status = model.Status then
                        selected status
                    else
                        unselected status
                Shared.LevelItem.Element el
                
            [ control AllTransactions
              control PendingTransactions
              control ClearedTransactions ]
        
        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            // Balances
            Shared.evenlySpacedLevel [
                Shared.LevelItem.HeadingAndTitle ("Balance", Format.amountWithDollarSign model.Sum.Sum)
                Shared.LevelItem.HeadingAndTitle ("Pending", Format.amountWithDollarSign model.Sum.PendingSum)
                Shared.LevelItem.HeadingAndTitle ("Actual", Format.amountWithDollarSign model.Sum.ClearedSum)
            ]
            
            // Controls
            Shared.level [
                Shared.LeftLevel statusControls
                Shared.RightLevel [
                    Shared.LevelItem.Element (a [_href "/home/clear"; _class "button is-light"] [str "Clear All"])
                    Shared.LevelItem.Element (a [_href "/home/adjust-balance"; _class "button is-light"] [str "Adjust Balance"])
                    Shared.LevelItem.Element (a [_href "/home/new"; _class "button is-success"] [str "New Transaction"])
                ]
            ]
            
            // List of transactions
            Shared.table [
                Shared.TableHead [
                    Shared.TableCell (str "#")
                    Shared.TableCell (str "Date")
                    Shared.TableCell (str "Amount")
                    Shared.TableCell (str "Name")
                    Shared.TableCell (str "Type")
                    Shared.TableCell (str "Cleared")
                ]
                Shared.TableBody [
                    for (index, transaction) in Seq.indexed model.Transactions do
                        yield Shared.TableRow [
                            Shared.TableCell (index + 1 |> string |> str)
                            Shared.TableCell (Format.date transaction.DateCreated |> str)
                            Shared.TableCell (amountCell transaction.Type transaction.Amount)
                            Shared.TableCell (a [_href (sprintf "/home/%i" transaction.Id)] [str transaction.Name])
                            Shared.TableCell (typeCell transaction.Type)
                            Shared.TableCell (clearedStatusCell transaction.Status)
                        ]
                ]
            ]
            
            Shared.pagination model.Status model.Page model.TotalPages
        ]
        
    let createOrEditTransactionPage (model : TransactionViewModel) : XmlNode =
        let deleteButton =
            match model with
            | ExistingTransaction (id, _) ->
                Form.Element.Button [
                    Form.ButtonText "Delete"
                    Form.ButtonFormAction (sprintf "/home/%i/delete" id)
                    Form.Color Form.ButtonColor.Danger
                    Form.Type Form.Submit ]
                |> Some 
            | _ ->
                None 
        
        let model, title, buttonText =
            match model with
            | NewTransaction n ->
                n, "New Transaction", "Create Transaction"
            | ExistingTransaction (id, e) ->
                e, sprintf "Edit Transaction #%i" id, "Update Transaction"
        let date =
            String.defaultValue "" model.DateCreated
        let placeholderDate =
            Format.date System.DateTimeOffset.Now
                
        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (Shared.title title)
                ]
                Shared.RightLevel [
                     G.a [A._href "/home"; A._class "button"] [
                        G.str "Cancel" ]
                     |> Shared.LevelItem.Element
                ]
            ]
            Form.create [Form.Method Form.Post; Form.AutoComplete false] [
                Form.Element.SelectBox [
                    Form.SelectOption.LabelText "Transaction Type"
                    Form.SelectOption.Value model.Type
                    Form.SelectOption.HtmlName "transactionType"
                    Form.SelectOption.Options [
                        {| Value = "debit"; Label = "Debit/Charge"; Selected = model.Type = "debit" |}
                        {| Value = "credit"; Label = "Credit/Deposit"; Selected = model.Type = "credit" |}
                    ]
                ]
                
                Form.Element.TextInput [
                    Form.Placeholder "Supermarket purchase"
                    Form.LabelText "Name or description"
                    Form.HtmlName "name"
                    Form.Required
                    Form.Value model.Name ]
                
                // Group the amount and check number inputs together
                Form.Element.Group [
                    Form.Element.NumberInput [
                        Form.Placeholder "0.00"
                        Form.LabelText "Amount"
                        Form.HtmlName "amount"
                        Form.Min 0.01M
                        // By setting the step to 0.01, the user cannot enter more than two decimal places
                        Form.Step 0.01M
                        Form.Required
                        Form.Value model.Amount ]
                    
                    Form.Element.TextInput [
                        Form.Placeholder "1234"
                        Form.LabelText "Check Number (optional)"
                        Form.HtmlName "checkNumber"
                        Form.Value model.CheckNumber ]
                ]
                
                // Group the date inputs together
                Form.Element.Group [
                    Form.Element.DateInput [
                        Form.Placeholder placeholderDate
                        Form.LabelText "Date"
                        Form.Value date
                        Form.HtmlName "date"
                        Form.Required ]
                    
                    Form.Element.DateInput [
                        Form.Placeholder placeholderDate
                        Form.LabelText "Date Cleared (optional)"
                        Form.Value model.ClearDate
                        Form.HtmlName "clearDate" ]
                ]
                
                Form.Element.MaybeError model.Error
                
                // Group the buttons together
                Form.Element.Group [
                    deleteButton
                    // Use an empty div so we still have two columns
                    |> Option.defaultValue Form.Element.EmptyDiv
                    
                    Form.Element.Button [
                        Form.ButtonText buttonText
                        Form.Alignment Form.ButtonAlignment.Right 
                        Form.Color Form.ButtonColor.Success
                        Form.Type Form.Submit ]
                ]
            ]
        ]

namespace FoxyBalance.Server.Views

open FoxyBalance.Server.Views.Components
open FoxyBalance.Server.Models.ViewModels
open Giraffe.GiraffeViewEngine

module Home =
    let homePage (model : HomePageViewModel) : XmlNode =
        let title = sprintf "Transactions - Page %i" model.Page
        
        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            // Balances
            Shared.evenlySpacedLevel [
                Shared.LevelItem.HeadingAndTitle ("Pending", sprintf "$%.2M" model.Sum.PendingSum)
                Shared.LevelItem.HeadingAndTitle ("Balance", sprintf "$%.2M" model.Sum.Sum)
                Shared.LevelItem.HeadingAndTitle ("Actual", sprintf "$%.2M" model.Sum.ClearedSum)
            ]
            
            // Controls
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (strong [] [str "All"])
                    Shared.LevelItem.Element (a [_href "/home?status=pending"] [str "Pending"])
                    Shared.LevelItem.Element (a [_href "/home?status=cleared"] [str "Cleared"])
                ]
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
                    Shared.TableCell (str "Type")
                    Shared.TableCell (str "Cleared")
                    Shared.TableCell (str "Name")
                ]
                Shared.TableBody [
                    for (index, transaction) in Seq.indexed model.Transactions do
                        yield Shared.TableRow [
                            Shared.TableCell (index + 1 |> string |> str)
                            Shared.TableCell (transaction.DateCreated.ToString "yyyy-MM-dd" |> str)
                            Shared.TableCell (sprintf "$%.2M" transaction.Amount |> str)
                            Shared.TableCell (sprintf "NYI" |> str)
                            Shared.TableCell (sprintf "NYI" |> str)
                            Shared.TableCell (a [_href (sprintf "/home/%i" transaction.Id)] [str transaction.Name])
                        ]
                ]
            ]
            
            Shared.pagination model.Page model.TotalPages
        ]

    let newTransactionPage (model : NewTransactionViewModel) : XmlNode =
        let title = "New Transaction"
        let date = String.defaultValue "" model.DateCreated
        let placeholderDate = System.DateTimeOffset.Now.ToString "yyyy-MM-dd"
        
        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            Form.create [Form.Method Form.Post; Form.AutoComplete false] [
                Form.Element.Title "New Transaction"
                
                Form.Element.SelectBox [
                    Form.SelectOption.LabelText "Transaction Type"
                    Form.SelectOption.Value "debit"
                    Form.SelectOption.HtmlName "transactionType"
                    Form.SelectOption.Options [
                        {| Value = "debit"; Label = "Debit/Charge" |}
                        {| Value = "credit"; Label = "Credit/Deposit" |}
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
                    Form.Element.Button [
                        Form.ButtonText "Save Transaction"
                        Form.Color Form.ButtonColor.Success
                        Form.Type Form.Submit ]
                    
                    Form.Element.Button [
                        Form.ButtonText "Cancel"
                        Form.Shade Form.Light
                        Form.Type (Form.ButtonType.Link "/home") ]
                ]
            ]
        ]
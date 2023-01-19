namespace FoxyBalance.Server.Views

open FoxyBalance.Database.Models
open FoxyBalance.Server.Views.Components
open FoxyBalance.Server.Models.ViewModels
open Giraffe.ViewEngine
module G = HtmlElements
module A = Attributes

module Income =
    let homePage (model : IncomeViewModel) : XmlNode =
        let title =
            $"Income - Page {model.Page}"
        
        let hasIgnoredRecords =
            Seq.exists (fun (r: IncomeRecord) -> r.Ignored) model.IncomeRecords
        
        let RecordLink (record: IncomeRecord) =
            a [_href $"/income/{record.Id}"; _title (Format.incomeSourceCustomerDescription record.Source)] [
                str (Format.incomeSourceDescription record.Source)
            ]
        
        let yearSelector =
            let unselected (year: int) =
                a [_href $"/income?year={year}"] [str (string year)]
            let selected (year: int) =
                strong [] [str (string year)]
            let control (year: int) =
                let el = 
                    if year = model.TaxYear then
                        selected year
                    else
                        unselected year
                Shared.LevelItem.Element el

            model.TaxYears
            |> List.ofSeq
            |> List.map (fun year -> control year.TaxYear)
            |> List.append [ Shared.LevelItem.Element (str "Tax Year") ]

        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            
            // Balances
            Shared.evenlySpacedLevel [
                Shared.LevelItem.HeadingAndTitle ("Total Income", model.Summary.TotalSales |> (Format.toDecimal >> Format.amountWithDollarSign))
                Shared.LevelItem.HeadingAndTitle ("Total Fees", model.Summary.TotalFees |> (Format.toDecimal >> Format.amountWithDollarSign))
                Shared.LevelItem.HeadingAndTitle ("Total Net", model.Summary.TotalNetShare |> (Format.toDecimal >> Format.amountWithDollarSign))
                Shared.LevelItem.HeadingAndTitle ("Estimated Taxes", model.Summary.TotalEstimatedTax |> (Format.toDecimal >> Format.amountWithDollarSign))
                Shared.LevelItem.HeadingAndTitle ("Tax Rate", model.Summary.TaxYear.TaxRate |> (Format.toDecimal >> Format.percentage))
            ]
            
            p [] [
                small [] [
                    str $"Total records: {model.Summary.TotalRecords}"
                    br []
                    str "Last synced: 3 hours ago"
                ]
            ]
            
            // Controls
            Shared.level [
                Shared.LeftLevel yearSelector
                Shared.RightLevel [
                    Shared.LevelItem.Element (a [_href "/income/new"; _class "button is-light"] [str "Manual Transaction"])
                    Shared.LevelItem.Element (a [_href "/income/sync"; _class "button is-success"] [str "Sync Income"])
                ]
            ]
            
            // List of transactions
            Shared.table [
                Shared.TableHead [
                    Shared.TableCell (str "#")
                    if hasIgnoredRecords then Shared.TableCell (str "Ignored")
                    Shared.TableCell (str "Date")
                    Shared.TableCell (str "Source")
                    Shared.TableCell (str "Description")
                    Shared.TableCell (str "Amount")
                    Shared.TableCell (str "Fee")
                    Shared.TableCell (str "Net")
                    Shared.TableCell (str "Tax")
                ]
                Shared.TableBody [
                    for (index, record) in Seq.indexed model.IncomeRecords do
                        yield Shared.TableRow [
                            Shared.TableCell (index + 1 |> string |> str)
                            if hasIgnoredRecords then
                                Shared.TableCell (if record.Ignored then span [_class "ignored"] [str "Ignored"] else str "")
                            Shared.TableCell (Format.date record.SaleDate |> str)
                            Shared.TableCell (record.Source |> Format.incomeSourceType |> str)
                            Shared.TableCell (RecordLink record)
                            Shared.TableCell (record.SaleAmount |> Format.toDecimal |> Format.amountWithDollarSign |> str)
                            Shared.TableCell (record.PlatformFee + record.ProcessingFee |> Format.toDecimal |> Format.amountWithDollarSign |> str)
                            Shared.TableCell (record.NetShare |> Format.toDecimal |> Format.amountWithDollarSign |> str)
                            Shared.TableCell (span [_class "has-text-right"] [record.EstimatedTax |> Format.toDecimal |> Format.amountWithDollarSign |> str])
                        ]
                ]
            ]
            
            Shared.pagination {
                StatusFilter = AllTransactions
                CurrentPage = model.Page
                MaxPages = model.TotalPages
                RouteType = Income
            }
        ]
        
    let syncShopifySalesPage (model : SyncShopifySalesViewModel) =
        let title = "Sync income"
        
        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (Shared.title title)
                ]
                Shared.RightLevel [
                     G.a [A._href "/income"; A._class "button"] [
                        G.str "Cancel" ]
                     |> Shared.LevelItem.Element
                ]
            ]
            Form.create [Form.Method Form.Post; Form.AutoComplete false; Form.EncType Form.Multipart] [
                Form.Element.CheckboxInput [
                    Form.Checked model.SyncGumroadIncome
                    Form.CheckboxOption.HtmlName "syncGumroad"
                    Form.CheckboxOption.CheckboxText "Sync Gumroad income" ]
                
                Form.Element.FileInput [
                    Form.LabelText "Paypal transactions CSV file"
                    Form.HelpText "Upload your Paypal transactions CSV file here, and invoice income will be parsed by Foxy Balance to be sorted into the appropriate tax year."
                    Form.Accept ".csv"
                    Form.HtmlName "paypalCsvFile" ]
                
                Form.Element.FileInput [
                    Form.LabelText "Shopify earnings CSV file"
                    Form.HelpText "Upload your Shopify earnings CSV file here, and the earnings will be parsed by Foxy Balance and sorted by the appropriate tax year."
                    Form.Accept ".csv"
                    Form.HtmlName "shopifyCsvFile" ]

                Form.Element.MaybeError model.Error

                // Group the buttons together
                Form.Element.Group [
                    // Use an empty div so we still have two columns
                    Form.Element.EmptyDiv
                    
                    Form.Element.Button [
                        Form.ButtonText "Sync"
                        Form.Alignment Form.ButtonAlignment.Right 
                        Form.Color Form.ButtonColor.Success
                        Form.Type Form.Submit ]
                ]
            ]
        ]

    let createRecordPage (model: NewIncomeRecordViewModel): XmlNode =
        let title = "Create Income Record"
        let placeholderDate = Format.date System.DateTimeOffset.Now
        
        Shared.pageContainer "New " Shared.Authenticated Shared.WrappedInSection [
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (Shared.title title)
                ]
                Shared.RightLevel [
                     G.a [A._href "/income"; A._class "button"] [
                        G.str "Cancel" ]
                     |> Shared.LevelItem.Element
                ]
            ]
            
            Form.create [Form.Method Form.Post; Form.AutoComplete false] [
                Form.Group [
                    Form.Element.DateInput [
                        Form.Placeholder placeholderDate
                        Form.LabelText "Date"
                        Form.Value model.SaleDate
                        Form.HtmlName "saleDate"
                        Form.Required ]

                    Form.Element.SelectBox [
                        Form.SelectOption.LabelText "Income Source"
                        Form.SelectOption.HtmlName "source"
                        Form.SelectOption.Options [ {| Label = "Manual Transaction"; Value = "manual"; Selected = true |} ]
                        Form.SelectOption.Value "manual" ]
                ]
                
                Form.Element.TextInput [
                    Form.Placeholder "Custom cash to local business"
                    Form.LabelText "Sale Description"
                    Form.HtmlName "description"
                    Form.Required
                    Form.Value model.Description
                ]
                
                Form.Element.TextInput [
                    Form.Placeholder "Tomorrow Corporation"
                    Form.LabelText "Customer"
                    Form.HtmlName "customerDescription"
                    Form.Required
                    Form.Value model.CustomerDescription
                ]
                
                Form.Element.NumberInput [
                    Form.Placeholder "0.00"
                    Form.LabelText "Sale Amount"
                    Form.HtmlName "saleAmount"
                    Form.Min 0.01M
                    // By setting the step to 0.01, the user cannot enter more than two decimal places
                    Form.Step 0.01M
                    Form.Required
                    Form.Value model.SaleAmount ]
                
                Form.Element.NumberInput [
                    Form.Placeholder "0.00"
                    Form.LabelText "Platform Fee"
                    Form.HelpText "The Platform Fee refers to fees applied by the platform, e.g. Gumroad or Shopify fees. These are generally not transfer fees."
                    Form.HtmlName "platformFee"
                    Form.Min 0.00M
                    // By setting the step to 0.01, the user cannot enter more than two decimal places
                    Form.Step 0.01M
                    Form.Required
                    Form.Value model.PlatformFee ]
                
                Form.Element.NumberInput [
                    Form.Placeholder "0.00"
                    Form.LabelText "Processing Fee"
                    Form.HelpText "The Processing Fee refers to fees applied for processing a transaction, e.g. for transfers between the platform and a bank account."
                    Form.HtmlName "processingFee"
                    Form.Min 0.00M
                    // By setting the step to 0.01, the user cannot enter more than two decimal places
                    Form.Step 0.01M
                    Form.Required
                    Form.Value model.ProcessingFee ]
                
                Form.Element.MaybeError model.Error
                
                // Group the buttons together
                Form.Element.Group [
                    Form.EmptyDiv
                    
                    Form.Element.Button [
                        Form.ButtonText "Save Record"
                        Form.Alignment Form.ButtonAlignment.Right 
                        Form.Color Form.ButtonColor.Success
                        Form.Type Form.Submit ]
                ]
            ]
        ]
        
    let recordDetailsPage (model: IncomeRecordViewModel): XmlNode =
        let record =
            model.IncomeRecord
        let title =
            match record.Source with
            | ManualTransaction _ -> $"Manually recorded transaction #{record.Id}"
            | x -> $"{Format.incomeSourceType x} transaction #{record.Id}"
        let subtitle =
            match record.Source with
            | Paypal x
            | Gumroad x
            | Shopify x
            | Stripe x -> Some x.TransactionId
            | _ -> None
            
        let actionButton =
            let actionProps =
                // Manual transactions can be deleted. All other transactions can only be ignored.
                match record.Source with
                | ManualTransaction _ ->
                    [ Form.ButtonText "Delete"
                      Form.ButtonFormAction $"/income/{record.Id}/delete"
                      Form.OnClick "return confirm('Are you sure you want to delete this income record? This action cannot be undone.')" ]
                | _ ->
                    [ Form.ButtonText (if record.Ignored then "Unignore" else "Ignore")
                      Form.ButtonFormAction $"/income/{record.Id}/ignore" ]

            Form.Element.Button [
                yield! actionProps
                Form.Color Form.ButtonColor.Danger
                Form.Type Form.Submit ]
        
        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            Shared.level [
                Shared.LeftLevel [
                    Shared.LevelItem.Element (Shared.title title)
                ]
                Shared.RightLevel [
                     Shared.LevelItem.Element (Form.create [Form.Method Form.Post] [actionButton])
                     
                     G.a [A._href "/income"; A._class "button"] [
                        G.str "Cancel" ]
                     |> Shared.LevelItem.Element
                ]
            ]
            
            hr []
            
            div [_class "columns"] [
                div [_class "column is-two-thirds"] [
                    Shared.subtitle (Format.incomeSourceDescription record.Source)
                    Shared.subtitle ("Customer: " + Format.incomeSourceCustomerDescription record.Source)
                    Shared.subtitle ("Sale date: " + Format.date record.SaleDate)
                    
                    subtitle
                    |> Option.map (fun sub ->
                        Shared.subtitleFromList [
                            str "Source transaction ID: "
                            abbr [_title sub] [str (Format.truncateStr(sub, 30))]
                        ]    
                    )
                    |> Shared.maybeEl
                ]

                div [_class "column is-one-third box"] [
                    Shared.level [
                        Shared.LeftLevel [
                            $"Sale on {Format.date record.SaleDate}"
                            |> str
                            |> Shared.LevelItem.Element
                        ]
                        Shared.RightLevel [
                            Format.toDecimal record.SaleAmount
                            |> Format.amountWithDollarSign
                            |> str
                            |> Shared.LevelItem.Element
                        ]
                    ]
                    Shared.level [
                        Shared.LeftLevel [
                            str "Platform fee"
                            |> Shared.LevelItem.Element
                        ]
                        Shared.RightLevel [
                            Format.toDecimal record.PlatformFee
                            |> Format.amountWithNegativeSign
                            |> str
                            |> Shared.LevelItem.Element
                        ]
                    ]
                    Shared.level [
                        Shared.LeftLevel [
                            str "Processing fee"
                            |> Shared.LevelItem.Element
                        ]
                        Shared.RightLevel [
                            Format.toDecimal record.ProcessingFee
                            |> Format.amountWithNegativeSign
                            |> str
                            |> Shared.LevelItem.Element
                        ]
                    ]
                    hr []
                    Shared.level [
                        Shared.LeftLevel [
                            str "Net share"
                            |> Shared.LevelItem.Element
                        ]
                        Shared.RightLevel [
                            Format.toDecimal record.NetShare
                            |> fun share -> (if share > 0M then Format.amountWithPositiveSign share else Format.amountWithNegativeSign share)
                            |> str
                            |> Shared.LevelItem.Element
                        ]
                    ]
                    hr []
                    Shared.level [
                        Shared.LeftLevel [
                            str "Estimated taxes"
                            |> Shared.LevelItem.Element
                        ]
                        Shared.RightLevel [
                            p [_class "estimated-tax"] [
                                Format.toDecimal record.EstimatedTax
                                |> Format.amountWithDollarSign
                                |> str
                                
                                if record.Ignored then
                                    span [_class "ignored"] [ str "(Ignored)" ]
                            ]
                            |> Shared.LevelItem.Element
                        ]
                    ]
                ]
            ]
        ]

namespace FoxyBalance.Server.Views

open FoxyBalance.Database.Models
open FoxyBalance.Server.Views.Components
open FoxyBalance.Server.Models.ViewModels
open Giraffe.ViewEngine
module G = HtmlElements
module A = Attributes

module Income =
    let homePage (model : IncomeViewModel) : XmlNode =
        let title = $"Income - Page {model.Page}"
        
        let RecordLink (record: IncomeRecord) =
            a [_href $"/income/{record.Id}"; _title (Format.incomeSourceCustomerDescription record.Source)] [
                str (Format.incomeSourceDescription record.Source)
            ]
        
        let yearSelector =
            let unselected year =
                a [_href $"/income?year={year}"] [str year]
            let selected year =
                strong [] [str year]
            let control year =
                let el = 
                    if year = "2022" then
                        selected year
                    else
                        unselected year
                Shared.LevelItem.Element el
            
            // TODO: get these tax years dynamically from the database
            [ "2022"
              "2021"
              "2020" ]    
            |> List.map control
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
                    Shared.LevelItem.Element (a [_href "/income/manual-transaction"; _class "button is-light"] [str "Manual Transaction"])
                    Shared.LevelItem.Element (a [_href "/income/sync"; _class "button is-success"] [str "Sync Income"])
                ]
            ]
            
            // List of transactions
            Shared.table [
                Shared.TableHead [
                    Shared.TableCell (str "#")
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
                            Shared.TableCell (Format.date record.SaleDate |> str)
                            Shared.TableCell (record.Source |> Format.incomeSourceType |> str)
                            Shared.TableCell (RecordLink record)
                            Shared.TableCell (record.SaleAmount |> Format.toDecimal |> Format.amountWithDollarSign |> str)
                            Shared.TableCell (record.PlatformFee + record.ProcessingFee |> Format.toDecimal |> Format.amountWithDollarSign |> str)
                            Shared.TableCell (record.NetShare |> Format.toDecimal |> Format.amountWithDollarSign |> str)
                            Shared.TableCell (record.EstimatedTax |> Format.toDecimal |> Format.amountWithDollarSign |> str)
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

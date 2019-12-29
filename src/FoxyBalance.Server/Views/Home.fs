namespace FoxyBalance.Server.Views

open FoxyBalance.Server.Models.ViewModels
open Giraffe.GiraffeViewEngine

module Home =
    let homePage (model : HomePageViewModel) : XmlNode =
        let title = sprintf "Transactions - Page %i" model.Page
        
        Shared.pageContainer title Shared.Authenticated Shared.WrappedInSection [
            // Balances
            Shared.evenlySpacedLevel [
                Shared.LevelItem.HeadingAndTitle ("Pending", sprintf "$%.2M" model.Sum.PendingSum)
                Shared.LevelItem.HeadingAndTitle ("Available", sprintf "$%.2M" model.Sum.Sum)
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
                    Shared.LevelItem.Element (a [_href "/home/new"; _class "button is-success"] [str "New Transaction"])
                ]
            ]
            
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

namespace FoxyBalance.Server.Views

open Giraffe.GiraffeViewEngine

[<AutoOpen>]
module ViewUtils = 
    let maybeEl nodeOption : XmlNode =
        match nodeOption with
        | Some el -> el
        | None -> emptyText
    
    let maybeErr errorMessage : XmlNode =
        errorMessage
        |> Option.map (fun msg -> p [_class "error red"] [str msg])
        |> maybeEl

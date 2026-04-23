module Program

open System
open FoxyBalance.CLI

[<EntryPoint>]
let main argv =
    try
        CommandRoot.entry argv
    with ex ->
        printfn "Error: %s" ex.Message
        ExitCodes.generalError

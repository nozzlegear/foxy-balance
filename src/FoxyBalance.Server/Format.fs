module Format
    open FoxyBalance.Database.Models

    let date (d : System.DateTimeOffset) =
        d.ToString "yyyy-MM-dd"
        
    let amount (d : decimal) =
        sprintf "%.2M" d
        
    let amountWithPositiveSign (d : decimal) =
        amount d
        |> sprintf "+%s"

    let amountWithNegativeSign (d : decimal) =
        amount d
        |> sprintf "-%s"
        
    let amountWithDollarSign (d : decimal) =
        amount d
        |> sprintf "$%s"

    let transactionType (t : TransactionType) =
        match t with
        | Credit ->
            "credit"
        | _ ->
            "debit"
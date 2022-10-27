module Format
    open FoxyBalance.Database.Models

    let date (d : System.DateTimeOffset) =
        d.ToString "yyyy-MM-dd"
        
    let amount (d : decimal) =
        $"%.2F{d}"
        
    let amountWithPositiveSign (d : decimal) =
        amount d
        |> sprintf "+%s"

    let amountWithNegativeSign (d : decimal) =
        amount d
        |> sprintf "-%s"
            
    let percentage (d : decimal) =
       $"%.0f{d * 100M}%%"
       
    let toDecimal (amountInPennies : int) =
        decimal amountInPennies / 100M
        
    let amountWithDollarSign (d : decimal) =
        amount d
        |> sprintf "$%s"

    let transactionType (t : TransactionType) =
        match t with
        | Credit ->
            "credit"
        | _ ->
            "debit"
            
    let incomeSourceType = function
        | Shopify _ ->
            "Shopify"
        | Gumroad _ ->
            "Gumroad"
        | Stripe _ ->
            "Stripe"
        | Paypal _ ->
            "Paypal Invoice"
        | ManualTransaction _ ->
            "Manual"
            
    let incomeSourceDescription = function
        | Shopify x ->
            x.Description
        | Gumroad x ->
            x.Description
        | Stripe x ->
            x.Description
        | Paypal x ->
            x.Description
        | ManualTransaction x ->
            x.Description

    let incomeSourceCustomerDescription = function
        | Shopify x
        | Gumroad x
        | Stripe  x
        | Paypal  x ->
            x.CustomerDescription
        | ManualTransaction x ->
            x.CustomerDescription
            |> Option.defaultValue "(No customer)"

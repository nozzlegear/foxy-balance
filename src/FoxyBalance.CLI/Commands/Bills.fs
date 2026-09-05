namespace FoxyBalance.CLI.Commands

open System
open FSharp.SystemCommandLine
open FSharp.SystemCommandLine.Input
open FoxyBalance.CLI
open FoxyBalance.CLI.Domain
open FoxyBalance.CLI.TokenStore
open FoxyBalance.CLI.Formatters

module Bills =

    /// `bills list`: GET /api/v1/bills
    let listCommand : System.CommandLine.Command =
        let activeOpt = option<bool> "--active" |> desc "Show only active bills" |> defaultValue false
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON" |> defaultValue false

        let action (active: bool, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)

                let path = if active then "/api/v1/bills?active=true" else "/api/v1/bills"
                let! result = client.GetCollectionAsync<RecurringBillDto>(path)

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok bills ->
                    if json then
                        printJson bills
                    else
                        printBills bills
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "list" {
            description "List recurring bills"
            inputs (activeOpt, jsonOpt)
            setAction action
        }

    /// `bills view <id>`: GET /api/v1/bills/{id}
    let viewCommand : System.CommandLine.Command =
        let idArg = argument<int64> "id" |> desc "Bill ID"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON" |> defaultValue false

        let action (id: int64, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let! result = client.GetAsync<RecurringBillDto>(sprintf "/api/v1/bills/%d" id)

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok bill ->
                    if json then
                        printJson bill
                    else
                        printBill bill
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "view" {
            description "View a single recurring bill"
            inputs (idArg, jsonOpt)
            setAction action
        }

    /// `bills create`: POST /api/v1/bills
    let createCommand : System.CommandLine.Command =
        let nameOpt = optionMaybe<string> "--name" |> desc "Bill name"
        let amountOpt = optionMaybe<string> "--amount" |> desc "Bill amount (e.g. 50.00)"
        let weekOpt = optionMaybe<string> "--week-of-month" |> desc "Week of month (1-4)"
        let dayOpt = optionMaybe<string> "--day-of-week" |> desc "Day of week (0=Sunday through 6=Saturday)"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON" |> defaultValue false

        let action (name: string option, amount: string option, week: string option, day: string option, json: bool) =
            async {
                let resolvedName =
                    match name with
                    | Some n when not (String.IsNullOrWhiteSpace n) -> n
                    | _ ->
                        printf "Name: "
                        Console.ReadLine()

                let resolvedAmount =
                    match amount with
                    | Some a when not (String.IsNullOrWhiteSpace a) -> a
                    | _ ->
                        printf "Amount: "
                        Console.ReadLine()

                let resolvedWeek =
                    match week with
                    | Some w when not (String.IsNullOrWhiteSpace w) -> w
                    | _ ->
                        printf "Week of month (1-4): "
                        Console.ReadLine()

                let resolvedDay =
                    match day with
                    | Some d when not (String.IsNullOrWhiteSpace d) -> d
                    | _ ->
                        printf "Day of week (0=Sunday, 6=Saturday): "
                        Console.ReadLine()

                let request: ApiRecurringBillRequest =
                    { Name = resolvedName
                      Amount = resolvedAmount
                      WeekOfMonth = resolvedWeek
                      DayOfWeek = resolvedDay }

                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let! result = client.PostAsync<RecurringBillDto>("/api/v1/bills", request)

                match result with
                | Error e ->
                    printfn "Error creating bill: %s" e
                    return ExitCodes.generalError
                | Ok bill ->
                    if json then
                        printJson bill
                    else
                        printBill bill
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "create" {
            description "Create a new recurring bill"
            inputs (nameOpt, amountOpt, weekOpt, dayOpt, jsonOpt)
            setAction action
        }

    /// `bills update <id>`: PUT /api/v1/bills/{id}
    let updateCommand : System.CommandLine.Command =
        let idArg = argument<int64> "id" |> desc "Bill ID"
        let nameOpt = optionMaybe<string> "--name" |> desc "Bill name"
        let amountOpt = optionMaybe<string> "--amount" |> desc "Bill amount"
        let weekOpt = optionMaybe<string> "--week-of-month" |> desc "Week of month (1-4)"
        let dayOpt = optionMaybe<string> "--day-of-week" |> desc "Day of week (0-6)"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON" |> defaultValue false

        let action (id: int64, name: string option, amount: string option, week: string option, day: string option, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let! existing = client.GetAsync<RecurringBillDto>(sprintf "/api/v1/bills/%d" id)

                match existing with
                | Error e ->
                    printfn "Error fetching bill: %s" e
                    return ExitCodes.generalError
                | Ok existingBill ->
                    let request: ApiRecurringBillRequest =
                        { Name =
                            match name with
                            | Some n when not (String.IsNullOrWhiteSpace n) -> n
                            | _ -> existingBill.Name
                          Amount =
                            match amount with
                            | Some a when not (String.IsNullOrWhiteSpace a) -> a
                            | _ -> string existingBill.Amount
                          WeekOfMonth =
                            match week with
                            | Some w when not (String.IsNullOrWhiteSpace w) -> w
                            | _ -> string existingBill.WeekOfMonth
                          DayOfWeek =
                            match day with
                            | Some d when not (String.IsNullOrWhiteSpace d) -> d
                            | _ -> string existingBill.DayOfWeek }

                    let! result = client.PutAsync<RecurringBillDto>(sprintf "/api/v1/bills/%d" id, request)

                    match result with
                    | Error e ->
                        printfn "Error updating bill: %s" e
                        return ExitCodes.generalError
                    | Ok bill ->
                        if json then
                            printJson bill
                        else
                            printBill bill
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "update" {
            description "Update an existing recurring bill"
            inputs (idArg, nameOpt, amountOpt, weekOpt, dayOpt, jsonOpt)
            setAction action
        }

    /// `bills delete <id>`: DELETE /api/v1/bills/{id}
    let deleteCommand : System.CommandLine.Command =
        let idArg = argument<int64> "id" |> desc "Bill ID"
        let forceOpt = option<bool> "--force" |> alias "-f" |> desc "Skip confirmation prompt" |> defaultValue false

        let action (id: int64, force: bool) =
            async {
                let confirmed =
                    if force then true
                    else
                        printf "Are you sure you want to delete bill %d? [y/N] " id
                        let response = Console.ReadLine()
                        response.Equals("y", StringComparison.OrdinalIgnoreCase)
                        || response.Equals("yes", StringComparison.OrdinalIgnoreCase)

                if not confirmed then
                    printfn "Cancelled."
                    return ExitCodes.success
                else
                    let baseUrl = getBaseUrl ()
                    let client = FoxyBalanceClient(baseUrl)
                    let! result = client.DeleteAsync(sprintf "/api/v1/bills/%d" id)
                    match result with
                    | Error e ->
                        printfn "Error: %s" e
                        return ExitCodes.generalError
                    | Ok () ->
                        printfn "Bill %d deleted." id
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "delete" {
            description "Delete a recurring bill"
            inputs (idArg, forceOpt)
            setAction action
        }

    /// `bills toggle <id>`: POST /api/v1/bills/{id}/toggle-active
    let toggleCommand : System.CommandLine.Command =
        let idArg = argument<int64> "id" |> desc "Bill ID"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON" |> defaultValue false

        let action (id: int64, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let! result = client.PostWithoutBodyAsync<RecurringBillDto>(sprintf "/api/v1/bills/%d/toggle-active" id)

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok bill ->
                    if json then
                        printJson bill
                    else
                        printBill bill
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "toggle" {
            description "Toggle a bill's active status"
            inputs (idArg, jsonOpt)
            setAction action
        }

    /// Build the `bills` parent command.
    let buildCommand : System.CommandLine.Command =
        command "bills" {
            description "Manage recurring bills"
            inputs context
            helpAction
            addCommands [ listCommand; viewCommand; createCommand; updateCommand; deleteCommand; toggleCommand ]
        }

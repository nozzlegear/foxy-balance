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
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (active: bool, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)

                let path = if active then "/api/v1/bills?active=true" else "/api/v1/bills"
                let! result = client.GetCollectionWithLinksAsync<RecurringBillDto>(path)

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok collection ->
                    if json then
                        printHalCollectionJson collection
                    else
                        printBillsWithLinks collection.Items
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "list" {
            description "List recurring bills"
            inputs (activeOpt, jsonOpt)
            setAction action
        }

    /// `bills view <id>`: GET /api/v1/bills/{id}
    /// Accepts either a numeric ID or a HATEOAS link href.
    let viewCommand : System.CommandLine.Command =
        let idArg = argument<string> "id" |> desc "Bill ID or HATEOAS link href"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (idOrLink: string, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let path = LinkResolver.resolvePath "/api/v1/bills" idOrLink
                let! result = client.GetResourceAsync<RecurringBillDto>(path)

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok resource ->
                    if json then
                        printHalResourceJson resource
                    else
                        printBillWithLinks resource
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "view" {
            description "View a single recurring bill (accepts ID or HATEOAS link)"
            inputs (idArg, jsonOpt)
            setAction action
        }

    /// `bills create`: POST /api/v1/bills
    let createCommand : System.CommandLine.Command =
        let nameOpt = optionMaybe<string> "--name" |> desc "Bill name"
        let amountOpt = optionMaybe<string> "--amount" |> desc "Bill amount (e.g. 50.00)"
        let weekOpt = optionMaybe<string> "--week-of-month" |> desc "Week of month (1-4)"
        let dayOpt = optionMaybe<string> "--day-of-week" |> desc "Day of week (0=Sunday through 6=Saturday)"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

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
                let! result = client.PostResourceAsync<RecurringBillDto>("/api/v1/bills", request)

                match result with
                | Error e ->
                    printfn "Error creating bill: %s" e
                    return ExitCodes.generalError
                | Ok resource ->
                    if json then
                        printHalResourceJson resource
                    else
                        printBillWithLinks resource
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "create" {
            description "Create a new recurring bill"
            inputs (nameOpt, amountOpt, weekOpt, dayOpt, jsonOpt)
            setAction action
        }

    /// `bills update <id>`: PUT /api/v1/bills/{id}
    /// Accepts either a numeric ID or a HATEOAS link href.
    let updateCommand : System.CommandLine.Command =
        let idArg = argument<string> "id" |> desc "Bill ID or HATEOAS link href"
        let nameOpt = optionMaybe<string> "--name" |> desc "Bill name"
        let amountOpt = optionMaybe<string> "--amount" |> desc "Bill amount"
        let weekOpt = optionMaybe<string> "--week-of-month" |> desc "Week of month (1-4)"
        let dayOpt = optionMaybe<string> "--day-of-week" |> desc "Day of week (0-6)"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (idOrLink: string, name: string option, amount: string option, week: string option, day: string option, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let path = LinkResolver.resolvePath "/api/v1/bills" idOrLink
                let! existing = client.GetResourceAsync<RecurringBillDto>(path)

                match existing with
                | Error e ->
                    printfn "Error fetching bill: %s" e
                    return ExitCodes.generalError
                | Ok existingResource ->
                    let existingBill = existingResource.Data
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

                    let! result = client.PutResourceAsync<RecurringBillDto>(path, request)

                    match result with
                    | Error e ->
                        printfn "Error updating bill: %s" e
                        return ExitCodes.generalError
                    | Ok resource ->
                        if json then
                            printHalResourceJson resource
                        else
                            printBillWithLinks resource
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "update" {
            description "Update an existing recurring bill (accepts ID or HATEOAS link)"
            inputs (idArg, nameOpt, amountOpt, weekOpt, dayOpt, jsonOpt)
            setAction action
        }

    /// `bills delete <id>`: DELETE /api/v1/bills/{id}
    /// Accepts either a numeric ID or a HATEOAS link href.
    let deleteCommand : System.CommandLine.Command =
        let idArg = argument<string> "id" |> desc "Bill ID or HATEOAS link href"
        let forceOpt = option<bool> "--force" |> alias "-f" |> desc "Skip confirmation prompt" |> defaultValue false

        let action (idOrLink: string, force: bool) =
            async {
                let displayId =
                    match LinkResolver.tryParseId idOrLink with
                    | Some id -> string id
                    | None -> idOrLink

                let confirmed =
                    if force then true
                    else
                        printf "Are you sure you want to delete bill %s? [y/N] " displayId
                        let response = Console.ReadLine()
                        response.Equals("y", StringComparison.OrdinalIgnoreCase)
                        || response.Equals("yes", StringComparison.OrdinalIgnoreCase)

                if not confirmed then
                    printfn "Cancelled."
                    return ExitCodes.success
                else
                    let baseUrl = getBaseUrl ()
                    let client = FoxyBalanceClient(baseUrl)
                    let path = LinkResolver.resolvePath "/api/v1/bills" idOrLink
                    let! result = client.DeleteAsync(path)
                    match result with
                    | Error e ->
                        printfn "Error: %s" e
                        return ExitCodes.generalError
                    | Ok () ->
                        printfn "Bill %s deleted." displayId
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "delete" {
            description "Delete a recurring bill (accepts ID or HATEOAS link)"
            inputs (idArg, forceOpt)
            setAction action
        }

    /// `bills toggle <id>`: POST /api/v1/bills/{id}/toggle-active
    /// Accepts either a numeric ID or a HATEOAS link href.
    let toggleCommand : System.CommandLine.Command =
        let idArg = argument<string> "id" |> desc "Bill ID or HATEOAS link href"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (idOrLink: string, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                // For toggle, the toggle-active endpoint is different from the resource path.
                // If a link href was passed, append /toggle-active; if numeric ID, build the full path.
                let path =
                    if LinkResolver.isLinkHref idOrLink then
                        let basePath = LinkResolver.resolvePath "/api/v1/bills" idOrLink
                        sprintf "%s/toggle-active" (basePath.TrimEnd('/'))
                    else
                        LinkResolver.resolvePath "/api/v1/bills" idOrLink + "/toggle-active"
                let! result = client.PostWithoutBodyResourceAsync<RecurringBillDto>(path)

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok resource ->
                    if json then
                        printHalResourceJson resource
                    else
                        printBillWithLinks resource
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "toggle" {
            description "Toggle a bill's active status (accepts ID or HATEOAS link)"
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

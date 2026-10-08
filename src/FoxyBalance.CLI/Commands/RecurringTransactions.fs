namespace FoxyBalance.CLI.Commands

open System
open FSharp.SystemCommandLine
open FSharp.SystemCommandLine.Input
open FoxyBalance.CLI
open FoxyBalance.CLI.Domain
open FoxyBalance.CLI.TokenStore
open FoxyBalance.CLI.Formatters

module RecurringTransactions =

    /// `recurring-transactions list`: GET /api/v1/bills
    let listCommand : System.CommandLine.Command =
        let activeOpt = option<bool> "--active" |> desc "Show only active recurring transactions" |> defaultValue false
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (active: bool, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)

                let path = if active then "/api/v1/bills?active=true" else "/api/v1/bills"
                let! result = client.GetCollectionWithLinksAsync(Codecs.recurringTransactionDtoDecoder, path)

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok collection ->
                    if json then
                        printHalCollectionJson (Codecs.recurringTransactionDtoEncoder, collection)
                    else
                        printRecurringTransactionsWithLinks collection.Items
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "list" {
            description "List recurring transactions"
            inputs (activeOpt, jsonOpt)
            setAction action
        }

    /// `recurring-transactions view <id>`: GET /api/v1/bills/{id}
    /// Accepts either a numeric ID or a HATEOAS link href.
    let viewCommand : System.CommandLine.Command =
        let idArg = argument<string> "id" |> desc "Recurring transaction ID or HATEOAS link href"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (idOrLink: string, json: bool) =
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let path = LinkResolver.resolvePath "/api/v1/bills" idOrLink
                let! result = client.GetResourceAsync(Codecs.recurringTransactionDtoDecoder, path)

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok resource ->
                    if json then
                        printHalResourceJson (Codecs.recurringTransactionDtoEncoder, resource)
                    else
                        printRecurringTransactionWithLinks resource
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "view" {
            description "View a single recurring transaction (accepts ID or HATEOAS link)"
            inputs (idArg, jsonOpt)
            setAction action
        }

    /// `recurring-transactions create`: POST /api/v1/bills
    let createCommand : System.CommandLine.Command =
        let nameOpt = optionMaybe<string> "--name" |> desc "Recurring transaction name"
        let amountOpt = optionMaybe<string> "--amount" |> desc "Amount (e.g. 50.00)"
        let scheduleTypeOpt = optionMaybe<string> "--schedule-type" |> desc "Schedule type: week or date [default: week]"
        let weekOpt = optionMaybe<string> "--week-of-month" |> desc "Week of month (1-4) [when schedule-type=week]"
        let dayOfWeekOpt = optionMaybe<string> "--day-of-week" |> desc "Day of week (0=Sunday through 6=Saturday) [when schedule-type=week]"
        let dayOfMonthOpt = optionMaybe<string> "--day-of-month" |> desc "Day of month (1-31) [when schedule-type=date]"
        let applyDateOpt = optionMaybe<string> "--apply-date" |> desc "Apply policy for days 29-31: early or late [when schedule-type=date]"
        let typeOpt = optionMaybe<string> "--type" |> desc "Transaction type: bill or income [default: bill]"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (ctx: ActionContext) =
            let name = ctx.ParseResult.GetValue<string option> "--name"
            let amount = ctx.ParseResult.GetValue<string option> "--amount"
            let scheduleType = ctx.ParseResult.GetValue<string option> "--schedule-type"
            let week = ctx.ParseResult.GetValue<string option> "--week-of-month"
            let dayOfWeek = ctx.ParseResult.GetValue<string option> "--day-of-week"
            let dayOfMonth = ctx.ParseResult.GetValue<string option> "--day-of-month"
            let applyDate = ctx.ParseResult.GetValue<string option> "--apply-date"
            let typ = ctx.ParseResult.GetValue<string option> "--type"
            let json = ctx.ParseResult.GetValue<bool> "--json"
            async {
                let resolve promptStr input =
                    match input with
                    | Some v when not (String.IsNullOrWhiteSpace v) -> v
                    | _ ->
                        printf "%s" promptStr
                        Console.ReadLine()

                let resolvedName = resolve "Name: " name
                let resolvedAmount = resolve "Amount (e.g. 50.00): " amount
                let resolvedScheduleType =
                    let s = resolve "Schedule type (week or date) [default: week]: " scheduleType
                    if String.IsNullOrWhiteSpace s then "week" else s.ToLowerInvariant()

                // Only prompt for fields relevant to the chosen schedule type.
                let weekOfMonthOpt, dayOfWeekOpt, dayOfMonthOpt, applyDateOpt =
                    if resolvedScheduleType = "date" then
                        let dom = resolve "Day of month (1-31): " dayOfMonth
                        let apply = resolve "Apply policy for days 29-31 (early or late, leave blank if N/A): " applyDate
                        None, None, Some dom, (if String.IsNullOrWhiteSpace apply then None else Some apply)
                    else
                        let w = resolve "Week of month (1-4): " week
                        let d = resolve "Day of week (0=Sunday, 6=Saturday): " dayOfWeek
                        Some w, Some d, None, None

                let request: ApiRecurringTransactionRequest =
                    { Name = resolvedName
                      Amount = resolvedAmount
                      ScheduleType = Some resolvedScheduleType
                      WeekOfMonth = weekOfMonthOpt
                      DayOfWeek = dayOfWeekOpt
                      DayOfMonth = dayOfMonthOpt
                      ApplyDate = applyDateOpt
                      Type = typ |> Option.map (fun t -> t.ToLowerInvariant()) }

                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let! result = client.PostResourceAsync(Codecs.apiRecurringTransactionRequestEncoder, Codecs.recurringTransactionDtoDecoder, "/api/v1/bills", request)

                match result with
                | Error e ->
                    printfn "Error creating recurring transaction: %s" e
                    return ExitCodes.generalError
                | Ok resource ->
                    if json then
                        printHalResourceJson (Codecs.recurringTransactionDtoEncoder, resource)
                    else
                        printRecurringTransactionWithLinks resource
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "create" {
            description "Create a new recurring transaction"
            inputs context
            addInputs [ nameOpt; amountOpt; scheduleTypeOpt; weekOpt; dayOfWeekOpt; dayOfMonthOpt; applyDateOpt; typeOpt; jsonOpt ]
            setAction action
        }

    /// `recurring-transactions update <id>`: PUT /api/v1/bills/{id}
    /// Accepts either a numeric ID or a HATEOAS link href.
    let updateCommand : System.CommandLine.Command =
        let idArg = argument<string> "id" |> desc "Recurring transaction ID or HATEOAS link href"
        let nameOpt = optionMaybe<string> "--name" |> desc "Recurring transaction name"
        let amountOpt = optionMaybe<string> "--amount" |> desc "Amount"
        let scheduleTypeOpt = optionMaybe<string> "--schedule-type" |> desc "Schedule type: week or date"
        let weekOpt = optionMaybe<string> "--week-of-month" |> desc "Week of month (1-4) [when schedule-type=week]"
        let dayOfWeekOpt = optionMaybe<string> "--day-of-week" |> desc "Day of week (0-6) [when schedule-type=week]"
        let dayOfMonthOpt = optionMaybe<string> "--day-of-month" |> desc "Day of month (1-31) [when schedule-type=date]"
        let applyDateOpt = optionMaybe<string> "--apply-date" |> desc "Apply policy for days 29-31: early or late [when schedule-type=date]"
        let typeOpt = optionMaybe<string> "--type" |> desc "Transaction type: bill or income"
        let jsonOpt = option<bool> "--json" |> desc "Output as JSON (includes HATEOAS links)" |> defaultValue false

        let action (ctx: ActionContext) =
            let idOrLink = ctx.ParseResult.GetValue<string> "id"
            let name = ctx.ParseResult.GetValue<string option> "--name"
            let amount = ctx.ParseResult.GetValue<string option> "--amount"
            let scheduleType = ctx.ParseResult.GetValue<string option> "--schedule-type"
            let week = ctx.ParseResult.GetValue<string option> "--week-of-month"
            let dayOfWeek = ctx.ParseResult.GetValue<string option> "--day-of-week"
            let dayOfMonth = ctx.ParseResult.GetValue<string option> "--day-of-month"
            let applyDate = ctx.ParseResult.GetValue<string option> "--apply-date"
            let typ = ctx.ParseResult.GetValue<string option> "--type"
            let json = ctx.ParseResult.GetValue<bool> "--json"
            async {
                let baseUrl = getBaseUrl ()
                let client = FoxyBalanceClient(baseUrl)
                let path = LinkResolver.resolvePath "/api/v1/bills" idOrLink
                let! existing = client.GetResourceAsync(Codecs.recurringTransactionDtoDecoder, path)

                match existing with
                | Error e ->
                    printfn "Error fetching recurring transaction: %s" e
                    return ExitCodes.generalError
                | Ok existingResource ->
                    let existingTransaction = existingResource.Data
                    // Resolve schedule type: use the explicit option, or fall back to the existing one.
                    let resolvedScheduleType =
                        match scheduleType with
                        | Some s when not (String.IsNullOrWhiteSpace s) -> s.ToLowerInvariant()
                        | _ -> existingTransaction.ScheduleType

                    // Build schedule fields based on the resolved schedule type.
                    // For fields not provided, fall back to the existing transaction's values.
                    let weekOfMonthOpt, dayOfWeekOpt, dayOfMonthOpt, applyDateOpt =
                        if resolvedScheduleType = "date" then
                            let dom =
                                match dayOfMonth with
                                | Some d when not (String.IsNullOrWhiteSpace d) -> d
                                | _ ->
                                    match existingTransaction.DayOfMonth with
                                    | Some d -> string d
                                    | None -> ""
                            let apply =
                                match applyDate with
                                | Some a when not (String.IsNullOrWhiteSpace a) -> Some a
                                | _ -> existingTransaction.ApplyDate
                            None, None, Some dom, apply
                        else
                            let w =
                                match week with
                                | Some w when not (String.IsNullOrWhiteSpace w) -> w
                                | _ -> string existingTransaction.WeekOfMonth
                            let d =
                                match dayOfWeek with
                                | Some d when not (String.IsNullOrWhiteSpace d) -> d
                                | _ -> string existingTransaction.DayOfWeek
                            Some w, Some d, None, None

                    let request: ApiRecurringTransactionRequest =
                        { Name =
                            match name with
                            | Some n when not (String.IsNullOrWhiteSpace n) -> n
                            | _ -> existingTransaction.Name
                          Amount =
                            match amount with
                            | Some a when not (String.IsNullOrWhiteSpace a) -> a
                            | _ -> string existingTransaction.Amount
                          ScheduleType = Some resolvedScheduleType
                          WeekOfMonth = weekOfMonthOpt
                          DayOfWeek = dayOfWeekOpt
                          DayOfMonth = dayOfMonthOpt
                          ApplyDate = applyDateOpt
                          Type =
                            match typ with
                            | Some t when not (String.IsNullOrWhiteSpace t) -> Some (t.ToLowerInvariant())
                            | _ -> Some existingTransaction.Type }

                    let! result = client.PutResourceAsync(Codecs.apiRecurringTransactionRequestEncoder, Codecs.recurringTransactionDtoDecoder, path, request)

                    match result with
                    | Error e ->
                        printfn "Error updating recurring transaction: %s" e
                        return ExitCodes.generalError
                    | Ok resource ->
                        if json then
                            printHalResourceJson (Codecs.recurringTransactionDtoEncoder, resource)
                        else
                            printRecurringTransactionWithLinks resource
                        return ExitCodes.success
            }
            |> Async.RunSynchronously
        command "update" {
            description "Update an existing recurring transaction (accepts ID or HATEOAS link)"
            inputs context
            addInputs [ idArg; nameOpt; amountOpt; scheduleTypeOpt; weekOpt; dayOfWeekOpt; dayOfMonthOpt; applyDateOpt; typeOpt; jsonOpt ]
            setAction action
        }

    /// `recurring-transactions delete <id>`: DELETE /api/v1/bills/{id}
    /// Accepts either a numeric ID or a HATEOAS link href.
    let deleteCommand : System.CommandLine.Command =
        let idArg = argument<string> "id" |> desc "Recurring transaction ID or HATEOAS link href"
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
                        printf "Are you sure you want to delete recurring transaction %s? [y/N] " displayId
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
                        printfn "Recurring transaction %s deleted." displayId
                        return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "delete" {
            description "Delete a recurring transaction (accepts ID or HATEOAS link)"
            inputs (idArg, forceOpt)
            setAction action
        }

    /// `recurring-transactions toggle <id>`: POST /api/v1/bills/{id}/toggle-active
    /// Accepts either a numeric ID or a HATEOAS link href.
    let toggleCommand : System.CommandLine.Command =
        let idArg = argument<string> "id" |> desc "Recurring transaction ID or HATEOAS link href"
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
                let! result = client.PostWithoutBodyResourceAsync(Codecs.recurringTransactionDtoDecoder, path)

                match result with
                | Error e ->
                    printfn "Error: %s" e
                    return ExitCodes.generalError
                | Ok resource ->
                    if json then
                        printHalResourceJson (Codecs.recurringTransactionDtoEncoder, resource)
                    else
                        printRecurringTransactionWithLinks resource
                    return ExitCodes.success
            }
            |> Async.RunSynchronously

        command "toggle" {
            description "Toggle a recurring transaction's active status (accepts ID or HATEOAS link)"
            inputs (idArg, jsonOpt)
            setAction action
        }

    /// Build the `recurring-transactions` parent command.
    let buildCommand : System.CommandLine.Command =
        command "recurring-transactions" {
            description "Manage recurring transactions"
            inputs context
            helpAction
            addCommands [ listCommand; viewCommand; createCommand; updateCommand; deleteCommand; toggleCommand ]
        }

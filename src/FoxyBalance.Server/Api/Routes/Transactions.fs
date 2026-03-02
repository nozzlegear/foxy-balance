namespace FoxyBalance.Server.Api.Routes

open System
open System.IO
open Giraffe
open Microsoft.Extensions.DependencyInjection
open FoxyBalance.Server.Api
open FoxyBalance.Server.Models.RequestModels
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open FoxyBalance.Sync
open FoxyBalance.Sync.Models

module Transactions =
    let private parseStatusFilter (value: string option) : StatusFilter =
        match value |> Option.map (fun s -> s.ToLowerInvariant()) with
        | Some "pending" -> PendingTransactions
        | Some "cleared" -> ClearedTransactions
        | _ -> AllTransactions

    let private statusFilterToQuery (status: StatusFilter) : string =
        match status with
        | PendingTransactions -> "&status=pending"
        | ClearedTransactions -> "&status=cleared"
        | AllTransactions -> ""

    let private toEditTransactionRequest (req: ApiTransactionRequest) : EditTransactionRequest =
        { Name = req.Name
          Amount = req.Amount
          Date = req.Date
          ClearDate = req.ClearDate
          TransactionType = req.TransactionType
          CheckNumber = req.CheckNumber }

    /// GET /api/v1/transactions
    /// List transactions with pagination and status filter
    let listHandler: HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let limit = 50

                let page =
                    ctx.TryGetQueryStringValue "page"
                    |> Option.bind (fun s ->
                        match Int32.TryParse(s) with
                        | true, v -> Some v
                        | _ -> None)
                    |> Option.defaultValue 1
                    |> max 1

                let status = ctx.TryGetQueryStringValue "status" |> parseStatusFilter

                let transactionDb = ctx.RequestServices.GetRequiredService<ITransactionDatabase>()

                let! transactions =
                    transactionDb.ListAsync(
                        session.UserId,
                        { Limit = limit
                          Offset = limit * (page - 1)
                          Order = Descending
                          Status = status }
                    )

                let! count = transactionDb.CountAsync(session.UserId, status)

                let totalPages =
                    if count % limit > 0 then
                        (count / limit) + 1
                    else
                        count / limit

                let transactionList = transactions |> Seq.toList
                let statusQuery = statusFilterToQuery status

                let itemLinks =
                    transactionList
                    |> List.map (fun t ->
                        HalBuilder.resource (ApiDtos.fromTransaction t) (HalBuilder.transactionLinks t.Id))

                let paginationLinks =
                    HalBuilder.paginationLinks "/api/v1/transactions" page totalPages statusQuery

                let collectionLinks =
                    [ yield! paginationLinks
                      yield LinkRel.Create, HalBuilder.linkWithMethod "POST" "/api/v1/transactions"
                      yield LinkRel.Import, HalBuilder.linkWithMethod "POST" "/api/v1/transactions/import"
                      yield LinkRel.Balance, HalBuilder.link "/api/v1/balance" ]

                let response = HalBuilder.collection itemLinks page totalPages count collectionLinks

                return! ApiRouteUtils.halJson response next ctx
            })

    /// GET /api/v1/transactions/{id}
    /// Get a single transaction
    let getHandler (transactionId: int64) : HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let transactionDb = ctx.RequestServices.GetRequiredService<ITransactionDatabase>()

                match! transactionDb.GetAsync(session.UserId, transactionId) with
                | None -> return! ApiRouteUtils.notFound "Transaction" next ctx
                | Some transaction ->
                    let halResponse =
                        HalBuilder.resource
                            (ApiDtos.fromTransaction transaction)
                            (HalBuilder.transactionLinks transactionId)

                    return! ApiRouteUtils.halJson halResponse next ctx
            })

    /// POST /api/v1/transactions
    /// Create a new transaction
    let createHandler: HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let! request = ctx.BindJsonAsync<ApiTransactionRequest>()

                match EditTransactionRequest.Validate(toEditTransactionRequest request) with
                | Error msg -> return! ApiRouteUtils.validationError msg next ctx
                | Ok partialTransaction ->
                    let transactionDb = ctx.RequestServices.GetRequiredService<ITransactionDatabase>()
                    let! created = transactionDb.CreateAsync(session.UserId, partialTransaction)

                    let halResponse =
                        HalBuilder.resource (ApiDtos.fromTransaction created) (HalBuilder.transactionLinks created.Id)

                    return! ApiRouteUtils.created halResponse next ctx
            })

    /// PUT /api/v1/transactions/{id}
    /// Update an existing transaction
    let updateHandler (transactionId: int64) : HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let transactionDb = ctx.RequestServices.GetRequiredService<ITransactionDatabase>()

                match! transactionDb.GetAsync(session.UserId, transactionId) with
                | None -> return! ApiRouteUtils.notFound "Transaction" next ctx
                | Some _ ->
                    let! request = ctx.BindJsonAsync<ApiTransactionRequest>()

                    match EditTransactionRequest.Validate(toEditTransactionRequest request) with
                    | Error msg -> return! ApiRouteUtils.validationError msg next ctx
                    | Ok partialTransaction ->
                        let! updated = transactionDb.UpdateAsync(session.UserId, transactionId, partialTransaction)

                        let halResponse =
                            HalBuilder.resource
                                (ApiDtos.fromTransaction updated)
                                (HalBuilder.transactionLinks transactionId)

                        return! ApiRouteUtils.halJson halResponse next ctx
            })

    /// DELETE /api/v1/transactions/{id}
    /// Delete a transaction
    let deleteHandler (transactionId: int64) : HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let transactionDb = ctx.RequestServices.GetRequiredService<ITransactionDatabase>()

                match! transactionDb.GetAsync(session.UserId, transactionId) with
                | None -> return! ApiRouteUtils.notFound "Transaction" next ctx
                | Some _ ->
                    do! transactionDb.DeleteAsync(session.UserId, transactionId)
                    return! ApiRouteUtils.noContent next ctx
            })

    /// POST /api/v1/transactions/import
    /// Bulk import transactions from CSV
    let importHandler: HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let! request = ctx.BindJsonAsync<ApiBulkImportRequest>()

                if String.IsNullOrWhiteSpace(request.Format) then
                    return! ApiRouteUtils.validationError "Format is required" next ctx
                elif String.IsNullOrWhiteSpace(request.Transactions) then
                    return! ApiRouteUtils.validationError "Transactions CSV data is required" next ctx
                else
                    match request.Format.ToLowerInvariant() with
                    | "capital-one" ->
                        let parser = ctx.RequestServices.GetRequiredService<CapitalOneTransactionParser>()
                        let transactionDb = ctx.RequestServices.GetRequiredService<ITransactionDatabase>()

                        use stream = new MemoryStream(Text.Encoding.UTF8.GetBytes(request.Transactions))

                        let partialTransactions =
                            parser.FromCsvStream(stream)
                            |> List.map (fun t ->
                                { Name = t.Description
                                  DateCreated = t.DateCreated
                                  Amount = t.Amount
                                  Status = TransactionStatus.Cleared t.DateCreated
                                  Type =
                                    if t.Type = CapitalOneTransactionType.Credit then
                                        TransactionType.Credit
                                    else
                                        TransactionType.Debit
                                  ImportId = Some t.Id
                                  RecurringBillId = None
                                  AutoGenerated = false })

                        let! importedCount = transactionDb.BulkCreateAsync(session.UserId, partialTransactions)
                        let totalCount = List.length partialTransactions

                        let result =
                            {| ImportedCount = importedCount
                               TotalCount = totalCount
                               SkippedCount = totalCount - importedCount |}

                        let halResponse =
                            HalBuilder.resource
                                result
                                [ LinkRel.Self, HalBuilder.link "/api/v1/transactions/import"
                                  LinkRel.Transactions, HalBuilder.link "/api/v1/transactions" ]

                        return! ApiRouteUtils.halJson halResponse next ctx
                    | format ->
                        return!
                            ApiRouteUtils.validationError
                                $"Unsupported format: {format}. Supported formats: capital-one"
                                next
                                ctx
            })

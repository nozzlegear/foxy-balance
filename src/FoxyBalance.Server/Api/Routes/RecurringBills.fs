namespace FoxyBalance.Server.Api.Routes

open System
open Giraffe
open Microsoft.Extensions.DependencyInjection
open FoxyBalance.Server.Api
open FoxyBalance.Server.Models.RequestModels
open FoxyBalance.Database.Interfaces

module RecurringBills =
    let private toEditBillRequest (req: ApiRecurringBillRequest) : EditRecurringBillRequest =
        { Name = req.Name
          Amount = req.Amount
          ScheduleType = req.ScheduleType
          WeekOfMonth = req.WeekOfMonth
          DayOfWeek = req.DayOfWeek
          DayOfMonth = req.DayOfMonth }

    /// GET /api/v1/bills
    /// List recurring bills
    let listHandler: HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let activeOnly =
                    ctx.TryGetQueryStringValue "active"
                    |> Option.map (fun v -> v.ToLowerInvariant() = "true")
                    |> Option.defaultValue false

                let billDb = ctx.RequestServices.GetRequiredService<IRecurringBillDatabase>()
                let! bills = billDb.ListAsync(session.UserId, activeOnly)

                let billList = bills |> Seq.toList

                let itemLinks =
                    billList
                    |> List.map (fun b ->
                        HalBuilder.resource (ApiDtos.fromRecurringBill b) (HalBuilder.billLinks b.Id))

                let activeQuery = if activeOnly then "&active=true" else ""

                let collectionLinks =
                    [ LinkRel.Self, HalBuilder.link $"/api/v1/bills?active={activeOnly}"
                      LinkRel.Create, HalBuilder.linkWithMethod "POST" "/api/v1/bills"
                      LinkRel.MatchSuggestions, HalBuilder.link "/api/v1/bills/match/suggestions"
                      LinkRel.Balance, HalBuilder.link "/api/v1/balance" ]

                let response =
                    HalBuilder.collection itemLinks 1 1 (List.length billList) collectionLinks

                return! ApiRouteUtils.halJson response next ctx
            })

    /// GET /api/v1/bills/{id}
    /// Get a single recurring bill
    let getHandler (billId: int64) : HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let billDb = ctx.RequestServices.GetRequiredService<IRecurringBillDatabase>()

                match! billDb.GetAsync(session.UserId, billId) with
                | None -> return! ApiRouteUtils.notFound "Bill" next ctx
                | Some bill ->
                    let halResponse =
                        HalBuilder.resource (ApiDtos.fromRecurringBill bill) (HalBuilder.billLinks billId)

                    return! ApiRouteUtils.halJson halResponse next ctx
            })

    /// POST /api/v1/bills
    /// Create a new recurring bill
    let createHandler: HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let! request = ctx.BindJsonAsync<ApiRecurringBillRequest>()

                match EditRecurringBillRequest.Validate(toEditBillRequest request) with
                | Error msg -> return! ApiRouteUtils.validationError msg next ctx
                | Ok partialBill ->
                    let billDb = ctx.RequestServices.GetRequiredService<IRecurringBillDatabase>()
                    let! created = billDb.CreateAsync(session.UserId, partialBill)

                    let halResponse =
                        HalBuilder.resource (ApiDtos.fromRecurringBill created) (HalBuilder.billLinks created.Id)

                    return! ApiRouteUtils.created halResponse next ctx
            })

    /// PUT /api/v1/bills/{id}
    /// Update an existing recurring bill
    let updateHandler (billId: int64) : HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let billDb = ctx.RequestServices.GetRequiredService<IRecurringBillDatabase>()

                match! billDb.GetAsync(session.UserId, billId) with
                | None -> return! ApiRouteUtils.notFound "Bill" next ctx
                | Some _ ->
                    let! request = ctx.BindJsonAsync<ApiRecurringBillRequest>()

                    match EditRecurringBillRequest.Validate(toEditBillRequest request) with
                    | Error msg -> return! ApiRouteUtils.validationError msg next ctx
                    | Ok partialBill ->
                        let! updated = billDb.UpdateAsync(session.UserId, billId, partialBill)

                        let halResponse =
                            HalBuilder.resource (ApiDtos.fromRecurringBill updated) (HalBuilder.billLinks billId)

                        return! ApiRouteUtils.halJson halResponse next ctx
            })

    /// DELETE /api/v1/bills/{id}
    /// Delete a recurring bill
    let deleteHandler (billId: int64) : HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let billDb = ctx.RequestServices.GetRequiredService<IRecurringBillDatabase>()

                match! billDb.GetAsync(session.UserId, billId) with
                | None -> return! ApiRouteUtils.notFound "Bill" next ctx
                | Some _ ->
                    do! billDb.DeleteAsync(session.UserId, billId)
                    return! ApiRouteUtils.noContent next ctx
            })

    /// POST /api/v1/bills/{id}/toggle-active
    /// Toggle a bill's active status
    let toggleActiveHandler (billId: int64) : HttpHandler =
        Middleware.withApiSession (fun session next ctx ->
            task {
                let billDb = ctx.RequestServices.GetRequiredService<IRecurringBillDatabase>()

                match! billDb.GetAsync(session.UserId, billId) with
                | None -> return! ApiRouteUtils.notFound "Bill" next ctx
                | Some bill ->
                    do! billDb.SetActiveAsync(session.UserId, billId, not bill.Active)
                    let! updated = billDb.GetAsync(session.UserId, billId)

                    match updated with
                    | Some updatedBill ->
                        let halResponse =
                            HalBuilder.resource (ApiDtos.fromRecurringBill updatedBill) (HalBuilder.billLinks billId)

                        return! ApiRouteUtils.halJson halResponse next ctx
                    | None -> return! ApiRouteUtils.notFound "Bill" next ctx
            })

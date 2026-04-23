namespace FoxyBalance.Server.Routes

open FoxyBalance.Database.Interfaces
open FoxyBalance.Sync
open FoxyBalance.Sync.Models
open Giraffe
open FoxyBalance.Server
open FoxyBalance.Database.Models
open FoxyBalance.Server.Models.RequestModels
open FoxyBalance.Server.Models.ViewModels
open FoxyBalance.Server.Services
open Microsoft.Extensions.DependencyInjection

module Views = FoxyBalance.Server.Views.Balance

module Balance =
    let homePageHandler : HttpHandler =
        RouteUtils.withSession(fun session next ctx -> task {
            let limit = 35
            let page =
                let parsedValue =
                    ctx.TryGetQueryStringValue "page"
                    |> Option.map int
                    |> Option.defaultValue 1
                if parsedValue < 0 then 1 else parsedValue
            let status =
                match ctx.TryGetQueryStringValue "status" |> Option.map String.toLower with
                | Some "pending" ->
                    PendingTransactions
                | Some "cleared" ->
                    ClearedTransactions
                | _ ->
                    AllTransactions
            let database = ctx.GetService<ITransactionDatabase>()
            let! transactions = database.ListAsync(session.UserId, { Limit = limit
                                                                     Offset = limit * (page - 1)
                                                                     Order = Descending
                                                                     Status = status })

            let! count = database.CountAsync(session.UserId, status)
            let! sum = database.SumAsync session.UserId 
            let model : HomePageViewModel =
                { Transactions = transactions
                  Sum = sum
                  Page = page
                  Status = status
                  TotalPages = if count % limit > 0 then (count / limit) + 1 else count / limit
                  TotalTransactions = count }
            
            return! htmlView (Views.homePage model) next ctx
        })
        
    let newTransactionHandler : HttpHandler =
        EditTransactionViewModel.Default
        |> NewTransaction
        |> Views.createOrEditTransactionPage
        |> htmlView
    
    let editTransactionHandler (transactionId : int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<ITransactionDatabase>()

            match! database.GetAsync(session.UserId, transactionId) with
            | Some transaction ->
                let! matchedTransaction =
                    match transaction.MatchedTransactionId with
                    | Some matchedId -> database.GetAsync(session.UserId, matchedId)
                    | None -> task { return None }

                let! candidates =
                    match matchedTransaction with
                    | Some _ -> task { return [] }
                    | None -> task {
                        let cutoffDate = System.DateTimeOffset.UtcNow.AddDays(-45.0)
                        let! all = database.ListTransactionMatchCandidatesAsync(session.UserId, cutoffDate)
                        let filtered =
                            all
                            |> List.ofSeq
                            |> List.filter (fun t -> t.Id <> transaction.Id)
                            |> List.filter (fun t ->
                                match transaction.Type with
                                | Bill _ ->
                                    // Current is Bill: show only non-Bill candidates
                                    match t.Type with
                                    | Bill _ -> false
                                    | _ -> true
                                | _ ->
                                    // Current is non-Bill: show Bills, or non-Bills where at least one is non-imported
                                    match t.Type with
                                    | Bill _ -> true
                                    | _ -> transaction.ImportId.IsNone || t.ImportId.IsNone)
                        return filtered
                    }

                let view =
                    (transactionId, EditTransactionViewModel.FromExistingTransaction transaction, matchedTransaction, candidates)
                    |> ExistingTransaction
                    |> Views.createOrEditTransactionPage
                    |> htmlView
                return! view next ctx
            | None ->
                return! (setStatusCode 404 >=> text "Not Found") next ctx
        })
        
    let private createOrEditTransaction (transactionId : int64 option) =
        RouteUtils.withSession (fun session next ctx -> task {
            let! model = ctx.BindFormAsync<EditTransactionRequest>()
            
            match EditTransactionRequest.Validate model with
            | Error msg ->
                let view =
                    let viewModel = EditTransactionViewModel.FromBadRequest model msg
                    
                    transactionId
                    |> Option.map (fun i -> ExistingTransaction (i, viewModel, None, []))
                    |> Option.defaultWith (fun _ -> NewTransaction viewModel)
                    |> Views.createOrEditTransactionPage
                    |> htmlView
                    
                return! (view >=> setStatusCode 422) next ctx
            | Ok partialTransaction ->
                let database = ctx.GetService<ITransactionDatabase>()
                
                do! match transactionId with
                    | Some transactionId ->
                        database.UpdateAsync(session.UserId, transactionId, partialTransaction)
                        |> Task.Ignore 
                    | None ->
                        database.CreateAsync(session.UserId, partialTransaction)
                        |> Task.Ignore 
                
                return! redirectTo false "/balance" next ctx
        })
        
    let private deleteTransaction transactionId =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<ITransactionDatabase>()
            
            do! database.DeleteAsync(session.UserId, transactionId)
            
            return! redirectTo false "/balance" next ctx 
        })
        
    let newTransactionPostHandler : HttpHandler =
        createOrEditTransaction None 

    let existingTransactionPostHandler (transactionId : int64) : HttpHandler =
        Some transactionId
        |> createOrEditTransaction

    let deleteTransactionPostHandler (transactionId : int64) : HttpHandler =
        deleteTransaction transactionId

    let matchTransactionPostHandler (transactionId : int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let! form = ctx.BindFormAsync<MatchTransactionRequest>()
            let database = ctx.GetService<ITransactionDatabase>()

            // Validate the other transaction belongs to the current user
            match! database.GetAsync(session.UserId, form.OtherTransactionId) with
            | None ->
                return! (setStatusCode 404 >=> text "Not Found") next ctx
            | Some otherTx ->
                // Prevent self-match
                if otherTx.Id = transactionId then
                    return! (setStatusCode 422 >=> text "Cannot match a transaction to itself.") next ctx
                else
                    do! database.MatchTransactionsAsync(session.UserId, transactionId, form.OtherTransactionId) |> Task.Ignore
                    return! redirectTo false $"/balance/{transactionId}" next ctx
        })

    let unmatchTransactionPostHandler (transactionId : int64) : HttpHandler =
        RouteUtils.withSession (fun session next ctx -> task {
            let database = ctx.GetService<ITransactionDatabase>()
            do! database.UnmatchTransactionAsync(session.UserId, transactionId) |> Task.Ignore
            return! redirectTo false $"/balance/{transactionId}" next ctx
        })

    let uploadTransactionsView : HttpHandler =
        UploadTransactionsViewModel.Default
        |> Views.Balance.uploadTransactionsPage
        |> htmlView

    let uploadTransactionsHandler : HttpHandler =
        let errorView msg: HttpHandler =
            { UploadTransactionsViewModel.Default with Error = Some msg }
            |> Views.Balance.uploadTransactionsPage
            |> htmlView
            >=> setStatusCode 422

        let mapToPartialTransaction (transaction: CapitalOneTransaction): PartialTransaction =
            { Name = transaction.Description
              DateCreated = transaction.DateCreated
              Amount = transaction.Amount
              Status = TransactionStatus.Cleared transaction.DateCreated
              Type = if transaction.Type = CapitalOneTransactionType.Credit
                     then TransactionType.Credit
                     else TransactionType.Debit
              ImportId = Some transaction.Id
              RecurringBillId = None
              AutoGenerated = false }


        RouteUtils.withSession (fun session next ctx -> task {
            match ctx.Request.Form.TryGetValue("source"), ctx.Request.Form.Files.GetFile("transactionsCsvFile") with
            | (false, source), _
            | (true, source), _ when string source <> "capital-one" ->
                return! (errorView $"Source must be \"Capital One\"; received \"{source}\".") next ctx
            | _, file when isNull file ->
                return! (errorView "CSV file is required.") next ctx
            | _, file ->
                let transactionParser = ctx.RequestServices.GetRequiredService<CapitalOneTransactionParser>()
                let transactionDatabase = ctx.RequestServices.GetRequiredService<ITransactionDatabase>()

                let parsedTransactions =
                    file.OpenReadStream ()
                    |> transactionParser.FromCsvStream
                    |> List.map mapToPartialTransaction

                let totalTransactions = List.length parsedTransactions
                let! importedTransactionsCount =
                    transactionDatabase.BulkCreateAsync(session.UserId, parsedTransactions)
                let existingTransactionsCount = totalTransactions - importedTransactionsCount

                return! ({ UploadTransactionsCompleteViewModel.Default with NewTransactions = importedTransactionsCount; ExistingTransactions = existingTransactionsCount }
                        |> Views.Balance.uploadTransactionsCompletePage
                        |> htmlView) next ctx
        })

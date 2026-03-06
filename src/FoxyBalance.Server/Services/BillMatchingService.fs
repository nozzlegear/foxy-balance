namespace FoxyBalance.Server.Services

open System
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models

type BillMatchingService(
    recurringBillDb: IRecurringBillDatabase,
    transactionDb: ITransactionDatabase) =

    /// Calculate the target date for a given week and day
    let calculateTargetDateForWeek (weekOfMonth: WeekOfMonth) (dayOfWeek: DayOfWeek) (referenceDate: DateTimeOffset) =
        let firstDayOfMonth = DateTimeOffset(referenceDate.Year, referenceDate.Month, 1, 0, 0, 0, referenceDate.Offset)

        // Find the first occurrence of the target day of week in the month
        let daysUntilTargetDay = (int dayOfWeek - int firstDayOfMonth.DayOfWeek + 7) % 7
        let firstTargetDayOfMonth = firstDayOfMonth.AddDays(float daysUntilTargetDay)

        // Add weeks to get to the target week
        let weeksToAdd = weekOfMonth.ToInt() - 1
        firstTargetDayOfMonth.AddDays(float (weeksToAdd * 7))

    /// Calculate a match score between a transaction and a recurring bill.
    /// history is a map of (UPPER(TRIM(name)), billId) -> number of prior confirmed matches.
    let calculateMatchScore (history: Map<string * RecurringBillId, int>) (transaction: Transaction) (bill: RecurringBill) =
        // Amount score
        let amountDiff = abs (transaction.Amount - bill.Amount)
        let amountScore =
            if amountDiff = 0M then 100.0M
            elif amountDiff < 0.01M then 90.0M
            elif amountDiff < 1.0M then 70.0M
            elif amountDiff < 5.0M then 50.0M
            else 0.0M

        // Calculate expected date for this bill in the transaction's month
        let expectedDate =
            match bill.Schedule with
            | WeekBased(week, day) -> calculateTargetDateForWeek week day transaction.DateCreated
            | DateBased _ -> transaction.DateCreated // For date-based bills, we'll use a simpler comparison

        // Date score based on proximity to expected date
        let daysDiff = abs ((transaction.DateCreated.Date - expectedDate.Date).Days)
        let dateScore =
            if daysDiff <= 1 then 100.0M
            elif daysDiff <= 3 then 80.0M
            elif daysDiff <= 7 then 60.0M
            elif daysDiff <= 14 then 40.0M
            else 0.0M

        // Name similarity (simple contains check)
        let nameScore =
            if transaction.Name.Contains(bill.Name, StringComparison.OrdinalIgnoreCase) ||
               bill.Name.Contains(transaction.Name, StringComparison.OrdinalIgnoreCase) then
                50.0M
            else 0.0M

        // History score: how many times has a transaction with this exact name (case-insensitive)
        // been confirmed as a match for this bill? Reaches full confidence at 2+ prior matches.
        let historyScore =
            let key = transaction.Name.Trim().ToUpperInvariant(), bill.Id
            match Map.tryFind key history with
            | None -> 0.0M
            | Some count -> min (decimal count * 50.0M) 100.0M

        // Weighted average: amount 45%, date 35%, name 5%, history 15%
        let totalScore =
            (amountScore * 0.45M) + (dateScore * 0.35M) + (nameScore * 0.05M) + (historyScore * 0.15M)

        { Transaction = transaction
          RecurringBill = bill
          MatchScore = totalScore }

    member this.GetMatchSuggestionsForUser(userId: UserId) =
        task {
            // Get all active recurring bills for the user
            let! bills = recurringBillDb.ListAsync(userId, true)

            // Get recent unmatched transactions (no RecurringBillId, not auto-generated, within 60 days).
            // Uses AllTransactions-equivalent logic in the DB since Capital One imports are Cleared.
            let cutoffDate = DateTimeOffset.UtcNow.AddDays(-60.0)
            let! unmatchedTransactions = transactionDb.ListMatchCandidatesAsync(userId, cutoffDate)
            let unmatchedTransactions = unmatchedTransactions |> List.ofSeq

            // Get history of confirmed matches grouped by (normalized name, bill id)
            let! history = transactionDb.GetBillMatchHistoryAsync(userId)

            // Calculate match scores for all combinations
            let candidates =
                [ for transaction in unmatchedTransactions do
                    for bill in bills do
                        yield calculateMatchScore history transaction bill ]
                |> List.filter (fun c -> c.MatchScore >= 40.0M)  // Only show decent matches
                |> List.sortByDescending (fun c -> c.MatchScore)

            return candidates
        }

    member this.MatchTransactionToBill(userId: UserId, transactionId: TransactionId, billId: RecurringBillId) =
        task {
            // Get the transaction
            let! transactionOpt = transactionDb.GetAsync(userId, transactionId)

            match transactionOpt with
            | None -> return Error "Transaction not found"
            | Some transaction ->
                // Find any auto-generated pending transaction for this bill in the same month
                let! allTransactions = transactionDb.ListAsync(userId, {
                    Limit = 1000
                    Offset = 0
                    Order = Descending
                    Status = PendingTransactions
                })

                let autoGeneratedForBill =
                    allTransactions
                    |> Seq.tryFind (fun t ->
                        t.RecurringBillId = Some billId &&
                        t.AutoGenerated &&
                        t.DateCreated.Year = transaction.DateCreated.Year &&
                        t.DateCreated.Month = transaction.DateCreated.Month)

                // If there's an auto-generated transaction, mark it as cleared
                match autoGeneratedForBill with
                | Some autoGen ->
                    let updatedAutoGen : PartialTransaction =
                        { Name = autoGen.Name
                          DateCreated = autoGen.DateCreated
                          Amount = autoGen.Amount
                          Status = Cleared transaction.DateCreated
                          Type = autoGen.Type
                          ImportId = autoGen.ImportId
                          RecurringBillId = autoGen.RecurringBillId
                          AutoGenerated = autoGen.AutoGenerated }

                    let! _ = transactionDb.UpdateAsync(userId, autoGen.Id, updatedAutoGen)
                    ()
                | None -> ()

                // Update the imported transaction to link it to the bill
                let updatedTransaction : PartialTransaction =
                    { Name = transaction.Name
                      DateCreated = transaction.DateCreated
                      Amount = transaction.Amount
                      Status = transaction.Status
                      Type = transaction.Type
                      ImportId = transaction.ImportId
                      RecurringBillId = Some billId
                      AutoGenerated = transaction.AutoGenerated }

                let! updated = transactionDb.UpdateAsync(userId, transactionId, updatedTransaction)

                return Ok updated
        }

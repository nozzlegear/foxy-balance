namespace FoxyBalance.Server.Services

open System
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models

type RecurringTransactionMatchingService(
    recurringTransactionDb: IRecurringTransactionDatabase,
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

    /// Calculate a match score between a transaction and a recurring transaction.
    /// history is a map of (UPPER(TRIM(name)), recurringTransactionId) -> number of prior confirmed matches.
    let calculateMatchScore (history: Map<string * RecurringTransactionId, int>) (transaction: Transaction) (rt: RecurringTransaction) =
        // Amount score
        let amountDiff = abs (transaction.Amount - rt.Amount)
        let amountScore =
            if amountDiff = 0M then 100.0M
            elif amountDiff < 0.01M then 90.0M
            elif amountDiff < 1.0M then 70.0M
            elif amountDiff < 5.0M then 50.0M
            else 0.0M

        // Calculate expected date for this recurring transaction in the transaction's month
        let (weekOfMonth, dayOfWeek) =
            match rt.Schedule with
            | ScheduleType.ByWeekOfMonth sched -> (sched.WeekOfMonth, sched.DayOfWeek)
            | ScheduleType.ByCalendarDate _ -> (WeekOfMonth.FirstWeek, DayOfWeek.Monday)
        let expectedDate = calculateTargetDateForWeek weekOfMonth dayOfWeek transaction.DateCreated

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
            if transaction.Name.Contains(rt.Name, StringComparison.OrdinalIgnoreCase) ||
               rt.Name.Contains(transaction.Name, StringComparison.OrdinalIgnoreCase) then
                50.0M
            else 0.0M

        // History score: how many times has a transaction with this exact name (case-insensitive)
        // been confirmed as a match for this recurring transaction? Reaches full confidence at 2+ prior matches.
        let historyScore =
            let key = transaction.Name.Trim().ToUpperInvariant(), rt.Id
            match Map.tryFind key history with
            | None -> 0.0M
            | Some count -> min (decimal count * 50.0M) 100.0M

        // Weighted average: amount 45%, date 35%, name 5%, history 15%
        let totalScore =
            (amountScore * 0.45M) + (dateScore * 0.35M) + (nameScore * 0.05M) + (historyScore * 0.15M)

        { Transaction = transaction
          RecurringTransaction = rt
          MatchScore = totalScore }

    member this.GetMatchSuggestionsForUser(userId: UserId) =
        task {
            // Get all active recurring transactions for the user
            let! recurringTransactions = recurringTransactionDb.ListAsync(userId, true)
            // Get recent unmatched transactions (no RecurringTransactionId, not auto-generated, within 60 days).
            let cutoffDate = DateTimeOffset.UtcNow.AddDays(-60.0)
            let! unmatchedTransactions = transactionDb.ListMatchCandidatesAsync(userId, cutoffDate)
            let unmatchedTransactions = unmatchedTransactions |> List.ofSeq

            // Get history of confirmed matches grouped by (normalized name, recurring transaction id)
            let! history = transactionDb.GetRecurringTransactionMatchHistoryAsync(userId)

            // Calculate match scores for all combinations
            let candidates =
                [ for transaction in unmatchedTransactions do
                    for rt in recurringTransactions do
                        yield calculateMatchScore history transaction rt ]
                |> List.filter (fun c -> c.MatchScore >= 40.0M)  // Only show decent matches
                |> List.sortByDescending (fun c -> c.MatchScore)

            return candidates
        }

    member this.MatchTransactionToRecurringTransaction(userId: UserId, transactionId: TransactionId, recurringTransactionId: RecurringTransactionId) =
        task {
            // Get the transaction
            let! transactionOpt = transactionDb.GetAsync(userId, transactionId)

            match transactionOpt with
            | None -> return Error "Transaction not found"
            | Some transaction ->
                // Find the most recent auto-generated pending transaction for this recurring transaction
                let! autoGeneratedForRt = transactionDb.GetAutoGeneratedForRecurringTransactionAsync(userId, recurringTransactionId)

                // If there's an auto-generated transaction, mark it as matched to the imported transaction
                // This makes it inert — excluded from listings and balance calculations
                match autoGeneratedForRt with
                | Some autoGen ->
                    let updatedAutoGen : PartialTransaction =
                        { Name = autoGen.Name
                          DateCreated = autoGen.DateCreated
                          Amount = autoGen.Amount
                          Status = Cleared transaction.DateCreated
                          Type = autoGen.Type
                          ImportId = autoGen.ImportId
                          RecurringTransactionId = autoGen.RecurringTransactionId
                          AutoGenerated = autoGen.AutoGenerated
                          MatchedTransactionId = Some transactionId }

                    let! _ = transactionDb.UpdateAsync(userId, autoGen.Id, updatedAutoGen)
                    ()
                | None -> ()

                // Update the imported transaction to link it to the recurring transaction
                let updatedTransaction : PartialTransaction =
                    { Name = transaction.Name
                      DateCreated = transaction.DateCreated
                      Amount = transaction.Amount
                      Status = transaction.Status
                      Type = transaction.Type
                      ImportId = transaction.ImportId
                      RecurringTransactionId = Some recurringTransactionId
                      AutoGenerated = transaction.AutoGenerated
                      MatchedTransactionId = None }

                let! updated = transactionDb.UpdateAsync(userId, transactionId, updatedTransaction)

                return Ok updated
        }

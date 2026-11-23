namespace FoxyBalance.Server.Api.Requests

/// Request type for matching a transaction to a recurring bill.
[<CLIMutable>]
type ApiMatchRequest =
    { TransactionId: string
      RecurringTransactionId: string }

namespace FoxyBalance.Server.Api.Requests

/// Token exchange request
[<CLIMutable>]
type TokenExchangeRequest = { ApiKey: string; ApiSecret: string }

/// Token refresh request
[<CLIMutable>]
type TokenRefreshRequest = { RefreshToken: string }

/// Transaction request for API
[<CLIMutable>]
type ApiTransactionRequest =
    { Name: string
      Amount: string
      Date: string
      ClearDate: string
      TransactionType: string
      CheckNumber: string }

/// Recurring transaction request for API
[<CLIMutable>]
type ApiRecurringTransactionRequest =
    { Name: string
      Amount: string
      WeekOfMonth: string option
      DayOfWeek: string option }

/// Match execution request
[<CLIMutable>]
type ApiMatchRequest = { TransactionId: int64; RecurringTransactionId: int64 }

/// Bulk import request
[<CLIMutable>]
type ApiBulkImportRequest =
    { Format: string; Transactions: string }

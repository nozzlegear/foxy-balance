namespace FoxyBalance.Database

open System
open FoxyBalance.Database.Models
open FoxyBalance.Database.Interfaces
open DustyTables

type TransactionDatabase(options : IDatabaseOptions) =
    let connection =
        Sql.connect options.ConnectionString
        |> Sql.timeout 90
    
    let mapRowToStatus (read : RowReader) : TransactionStatus =
        match read.string "Status" with
        | "Pending" ->
            Pending
        | "Cleared" ->
            // TODO: create a migration to convert the database column to DateTimeOffset
            DateTimeOffset (read.dateTime "DateCleared")
            |> Cleared
        | x ->
            failwith $"""Unrecognized Status type "{x}"."""
        
    let mapRowToDetails (read : RowReader) : TransactionType =
        match read.string "Type" with
        | "Debit" ->
            Debit
        | "Credit" ->
            Credit 
        | "Bill" ->
            { Recurring = read.bool "Recurring" }
            |> Bill
        | "Check" ->
            { CheckNumber = read.string "CheckNumber" }
            |> Check
        | x ->
            failwith $"""Unrecognized transaction type "{x}"."""
        
    let statusFilter = function
        | AllTransactions ->
            ""
        | PendingTransactions ->
            " AND [Status] = 'Pending' "
        | ClearedTransactions ->
            " AND [Status] = 'Cleared' "
                    
    let mapRowToTransaction (read : RowReader) : Transaction =
        { Id = read.int64 "Id"
          Name = read.string "Name"
          Amount = read.decimal "Amount"
          // TODO: create a migration to convert the database column to DateTimeOffset
          DateCreated = DateTimeOffset (read.dateTime "DateCreated")
          Status = mapRowToStatus read
          Type = mapRowToDetails read }
    
    let mapDetailsToSqlParams details =
        match details with
        | Check check ->
            {| typeStr = Sql.string "Check"
               checkNumber = Sql.string check.CheckNumber
               recurring = Sql.bool false |}
        | Bill bill ->
            {| typeStr = Sql.string "Bill"
               checkNumber = Sql.dbnull
               recurring = Sql.bool bill.Recurring |}
        | Debit ->
            {| typeStr = Sql.string "Debit"
               checkNumber = Sql.dbnull
               recurring = Sql.bool false |}
        | Credit ->
            {| typeStr = Sql.string "Credit"
               checkNumber = Sql.dbnull
               recurring = Sql.bool false |}
               
    let mapStatusToSqlParams status =
        match status with
        | Pending ->
            {| statusStr = Sql.string "Pending"
               dateCleared = Sql.dbnull |}
        | Cleared date -> 
            {| statusStr = Sql.string "Cleared"
               dateCleared = Sql.dateTimeOffset date |}
               
    interface ITransactionDatabase with
        member _.GetStatusAsync userId transactionId =
            connection
            |> Sql.query """
                SELECT [Status], [DateCleared]
                FROM [FoxyBalance_Transactions]
                WHERE [UserId] = @userId AND [Id] = @id
            """ 
            |> Sql.parameters [
                "userId", Sql.int userId
                "id", Sql.int64 transactionId 
            ]
            |> Sql.executeRowAsync mapRowToStatus

        member _.GetAsync userId transactionId =
            connection
            |> Sql.query """
                SELECT *
                FROM [FoxyBalance_Transactions]
                WHERE [UserId] = @userId AND [Id] = @id
            """
            |> Sql.parameters [
                "userId", Sql.int userId
                "id", Sql.int64 transactionId 
            ]
            |> Sql.executeAsync mapRowToTransaction
            |> Sql.tryExactlyOne
            
        member _.ExistsAsync userId transactionId =
            connection
            |> Sql.query """
                SELECT CASE WHEN EXISTS (
                    SELECT Id FROM [FoxyBalance_Transactions]
                    WHERE [UserId] = @userId AND [Id] = @id
                )
                THEN CAST(1 AS BIT)
                ELSE CAST(0 AS BIT) END
                """
            |> Sql.parameters [
                "userId", Sql.int userId
                "id", Sql.int64 transactionId
            ]
            |> Sql.executeRowAsync (fun read -> read.bool 0)
            
        member _.CreateAsync userId transaction =
            let details = mapDetailsToSqlParams transaction.Type
            let status = mapStatusToSqlParams transaction.Status
            let data = [
                "userId", Sql.int userId
                "dateCreated", Sql.dateTimeOffset transaction.DateCreated
                "name", Sql.string transaction.Name
                "amount", Sql.decimal transaction.Amount
                "type", details.typeStr
                "checkNumber", details.checkNumber
                "recurring", details.recurring
                "status", status.statusStr
                "dateCleared", status.dateCleared
            ]
            
            connection
            |> Sql.query """
                INSERT INTO [FoxyBalance_Transactions] (
                    UserId,
                    DateCreated,
                    Name,
                    Amount,
                    Type,
                    Recurring,
                    CheckNumber,
                    Status,
                    DateCleared
                ) OUTPUT INSERTED.Id VALUES(
                    @userId,
                    @dateCreated,
                    @name,
                    @amount,
                    @type,
                    @recurring,
                    @checkNumber,
                    @status,
                    @dateCleared
                )
            """
            |> Sql.parameters data
            |> Sql.executeRowAsync (fun read ->
                {
                    Id = read.int64 "Id"
                    Name = transaction.Name
                    Amount = transaction.Amount
                    DateCreated = transaction.DateCreated
                    Status = transaction.Status
                    Type = transaction.Type
                })
            
        member _.UpdateAsync userId transactionId transaction =
            let details = mapDetailsToSqlParams transaction.Type
            let status = mapStatusToSqlParams transaction.Status
            let data = [
                "userId", Sql.int userId
                "id", Sql.int64 transactionId
                "name", Sql.string transaction.Name
                "amount", Sql.decimal transaction.Amount
                "type", details.typeStr
                "checkNumber", details.checkNumber
                "recurring", details.recurring
                "status", status.statusStr
                "dateCleared", status.dateCleared
            ]
            
            connection
            |> Sql.query """
                UPDATE [FoxyBalance_Transactions]
                SET [Name] = @name,
                [Amount] = @amount,
                [Type] = @type,
                [CheckNumber] = @checkNumber,
                [Recurring] = @recurring,
                [Status] = @status,
                [DateCleared] = @dateCleared
                OUTPUT INSERTED.DateCreated
                WHERE [UserId] = @userId AND [Id] = @id
                """
            |> Sql.parameters data
            |> Sql.executeRowAsync (fun read ->
                {
                    Id = transactionId
                    Name = transaction.Name
                    Amount = transaction.Amount
                    DateCreated = read.dateTimeOffset "DateCreated"
                    Status = transaction.Status
                    Type = transaction.Type
                })
            
        member _.ListAsync userId options =
            let direction =
                match options.Order with
                | Ascending -> "ASC"
                | Descending -> "DESC"
            let whereClause =
                statusFilter options.Status
                |> sprintf "[UserId] = @userId %s"
                
            connection
            |> Sql.query $"""
                SELECT * FROM [FoxyBalance_Transactions]
                WHERE {whereClause}
                ORDER BY [DateCreated] {direction}, [Id] {direction}
                OFFSET @offset ROWS
                FETCH NEXT @limit ROWS ONLY
            """
            |> Sql.parameters [
                "userId", Sql.int userId
                "offset", Sql.int options.Offset
                "limit", Sql.int options.Limit
            ]
            |> Sql.executeAsync mapRowToTransaction
            |> Sql.map Seq.ofList
            
        member _.DeleteAsync userId transactionId =
            connection
            |> Sql.query "DELETE FROM [FoxyBalance_Transactions] WHERE [UserId] = @userId AND [Id] = @id" 
            |> Sql.parameters [
                "userId", Sql.int userId
                "id", Sql.int64 transactionId
            ]
            |> Sql.executeNonQueryAsync
            |> Sql.ignore
            
        member _.CountAsync userId status =
            let whereClause =
                statusFilter status
                |> sprintf "[UserId] = @userId %s"
            
            connection
            |> Sql.query $"""
                SELECT COUNT(Id)
                FROM [FoxyBalance_Transactions]
                WHERE {whereClause}
            """
            |> Sql.parameters [ "userId", Sql.int userId ]
            |> Sql.executeRowAsync (fun read -> read.int 0)
            
        member _.SumAsync userId =
            connection
            |> Sql.query """
                SELECT
                    SUM(CASE WHEN ([Status] = 'Cleared' AND [Type] <> 'Credit') THEN [Amount] ELSE 0 END) as ClearedDebit,
                    SUM(CASE WHEN ([Status] = 'Cleared' AND [Type] =  'Credit') THEN [Amount] ELSE 0 END) as ClearedCredit,
                    SUM(CASE WHEN ([Type] <> 'Credit') THEN [Amount] ELSE 0 END) as TotalDebit,
                    SUM(CASE WHEN ([Type] =  'Credit') THEN [Amount] ELSE 0 END) as TotalCredit
                FROM [FoxyBalance_Transactions]
                WHERE [UserId] = @userId
                """
            |> Sql.parameters [ "userId", Sql.int userId ]
            |> Sql.executeRowAsync (fun read ->
                let toDecimal columnName =
                    // A SQL Sum operation returns null if there are no records matched.
                    // Default to 0 when that happens.
                    read.decimalOrNone columnName
                    |> Option.defaultValue 0.0M
                            
                let totalCredit = toDecimal "TotalCredit"
                let totalDebit = toDecimal "TotalDebit"
                let clearedDebit = toDecimal "ClearedDebit"
                let clearedCredit = toDecimal "ClearedCredit"
                let pendingDebit = totalDebit - clearedDebit
                let pendingCredit = totalCredit - clearedCredit
                
                { Sum = totalCredit - totalDebit
                  PendingSum = pendingCredit - pendingDebit
                  ClearedSum = clearedCredit - clearedDebit
                  ClearedDebitSum = clearedDebit
                  ClearedCreditSum = clearedCredit
                  PendingDebitSum = pendingDebit
                  PendingCreditSum = pendingCredit }
            )
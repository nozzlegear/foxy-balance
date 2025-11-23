namespace FoxyBalance.Database

open System
open FoxyBalance.Database.Models
open FoxyBalance.Database.Interfaces
open Npgsql.FSharp

type TransactionDatabase(options : IDatabaseOptions) =
    let connection =
        Sql.connect options.ConnectionString

    let mapRowToStatus (read : RowReader) : TransactionStatus =
        match read.string "status" with
        | "Pending" ->
            Pending
        | "Cleared" ->
            // TODO: create a migration to convert the database column to DateTimeOffset
            DateTimeOffset (read.dateTime "datecleared")
            |> Cleared
        | x ->
            failwith $"""Unrecognized Status type "{x}"."""
        
    let mapRowToDetails (read : RowReader) : TransactionType =
        match read.string "type" with
        | "Debit" ->
            Debit
        | "Credit" ->
            Credit
        | "Bill" ->
            { Recurring = read.bool "recurring" }
            |> Bill
        | "Check" ->
            { CheckNumber = read.string "checknumber" }
            |> Check
        | x ->
            failwith $"""Unrecognized transaction type "{x}"."""
        
    let statusFilterParameter = function
        | AllTransactions ->
            SqlValue.String ""
        | PendingTransactions ->
            SqlValue.String "Pending"
        | ClearedTransactions ->
            SqlValue.String "Cleared"

    let mapRowToTransaction (read : RowReader) : Transaction =
        { Id = read.int64 "id"
          Name = read.string "name"
          Amount = read.decimal "amount"
          // TODO: create a migration to convert the database column to DateTimeOffset
          DateCreated = read.datetimeOffset "DateCreated"
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
               dateCleared = Sql.timestamptz date |}
               
    interface ITransactionDatabase with
        member _.GetStatusAsync userId transactionId =
            connection
            |> Sql.query """
                SELECT status, datecleared
                FROM foxybalance_transactions
                WHERE userid = @userId AND id = @id
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
                FROM foxybalance_transactions
                WHERE userid = @userId AND id = @id
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
                SELECT EXISTS (
                    SELECT id FROM foxybalance_transactions
                    WHERE userid = @userId AND id = @id
                ) as TransactionExists
                """
            |> Sql.parameters [
                "userId", Sql.int userId
                "id", Sql.int64 transactionId
            ]
            |> Sql.executeRowAsync (fun read -> read.bool "TransactionExists")
            
        member _.CreateAsync userId transaction =
            let details = mapDetailsToSqlParams transaction.Type
            let status = mapStatusToSqlParams transaction.Status
            let data = [
                "userId", Sql.int userId
                "dateCreated", Sql.timestamptz transaction.DateCreated
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
                INSERT INTO foxybalance_transactions (
                    userid,
                    datecreated,
                    name,
                    amount,
                    type,
                    recurring,
                    checknumber,
                    status,
                    datecleared
                ) VALUES (
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
                RETURNING id
            """
            |> Sql.parameters data
            |> Sql.executeRowAsync (fun read ->
                {
                    Id = read.int64 "id"
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
                UPDATE foxybalance_transactions
                SET name = @name,
                amount = @amount,
                type = @type,
                checknumber = @checkNumber,
                recurring = @recurring,
                status = @status,
                datecleared = @dateCleared
                WHERE userid = @userId AND id = @id
                RETURNING datecreated
                """
            |> Sql.parameters data
            |> Sql.executeRowAsync (fun read ->
                {
                    Id = transactionId
                    Name = transaction.Name
                    Amount = transaction.Amount
                    DateCreated = DateTimeOffset (read.dateTime "datecreated")
                    Status = transaction.Status
                    Type = transaction.Type
                })
            
        member _.ListAsync userId options =
            let direction =
                match options.Order with
                | Ascending -> "ASC"
                | Descending -> "DESC"
            let sql =
                """
                SELECT * FROM foxybalance_transactions
                WHERE userId = @userId
                """
            let sql =
                if not options.Status.IsAllTransactions then
                    sql
                else
                    sql + " AND status = @status"
            let sql =
              sql + """
              ORDER BY DateCreated @direction, id @direction
              OFFSET @offset ROWS
              FETCH NEXT @limit ROWS ONLY
              """

            connection
            |> Sql.query sql
            |> Sql.parameters [
                "userId", Sql.int userId
                "offset", Sql.int options.Offset
                "limit", Sql.int options.Limit
                "direction", Sql.string direction
                "status", statusFilterParameter options.Status
            ]
            |> Sql.executeAsync mapRowToTransaction
            |> Sql.map Seq.ofList
            
        member _.DeleteAsync userId transactionId =
            connection
            |> Sql.query "DELETE FROM foxybalance_transactions WHERE userid = @userId AND id = @id"
            |> Sql.parameters [
                "userId", Sql.int userId
                "id", Sql.int64 transactionId
            ]
            |> Sql.executeNonQueryAsync
            |> Sql.ignore
            
        member _.CountAsync userId status =
            let sql =
                """
                SELECT COUNT(id) as Total
                FROM foxybalance_transactions
                WHERE userId = @userId
                """
            let sql =
                if not status.IsAllTransactions then
                    sql
                else
                    sql + " AND status = @status"

            connection
            |> Sql.query sql
            |> Sql.parameters [
                "userId", Sql.int userId
                "status", statusFilterParameter status
            ]
            |> Sql.executeRowAsync (fun read -> read.int "Total")
            
        member _.SumAsync userId =
            connection
            |> Sql.query """
                SELECT
                    SUM(CASE WHEN (status = 'Cleared' AND type <> 'Credit') THEN amount ELSE 0 END) as cleareddebit,
                    SUM(CASE WHEN (status = 'Cleared' AND type =  'Credit') THEN amount ELSE 0 END) as clearedcredit,
                    SUM(CASE WHEN (type <> 'Credit') THEN amount ELSE 0 END) as totaldebit,
                    SUM(CASE WHEN (type =  'Credit') THEN amount ELSE 0 END) as totalcredit
                FROM foxybalance_transactions
                WHERE userid = @userId
                """
            |> Sql.parameters [ "userId", Sql.int userId ]
            |> Sql.executeRowAsync (fun read ->
                let toDecimal columnName =
                    // A SQL Sum operation returns null if there are no records matched.
                    // Default to 0 when that happens.
                    read.decimalOrNone columnName
                    |> Option.defaultValue 0.0M

                let totalCredit = toDecimal "totalcredit"
                let totalDebit = toDecimal "totaldebit"
                let clearedDebit = toDecimal "cleareddebit"
                let clearedCredit = toDecimal "clearedcredit"
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

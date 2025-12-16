namespace FoxyBalance.Database

open System
open FoxyBalance.Database.Models
open FoxyBalance.Database.Interfaces
open Npgsql
open Npgsql.FSharp

type TransactionDatabase(options : IDatabaseOptions) =
    let connection =
        Sql.connect options.ConnectionString

    let mapRowToStatus (read : RowReader) : TransactionStatus =
        match read.string "status" with
        | "Pending" -> Pending
        | "Cleared" -> Cleared (read.datetimeOffset "datecleared")
        | x -> failwith $"""Unrecognized Status type "{x}"."""
        
    let mapRowToDetails (read : RowReader) : TransactionType =
        match read.string "type" with
        | "Debit" -> Debit
        | "Credit" -> Credit
        | "Bill" -> Bill { Recurring = read.bool "recurring" }
        | "Check" -> Check { CheckNumber = read.string "checknumber" }
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
          DateCreated = read.datetimeOffset "datecreated"
          Status = mapRowToStatus read
          Type = mapRowToDetails read
          ImportId = read.stringOrNone "importid" }
    
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
               dateCleared = Sql.timestamptz (date.ToUniversalTime()) |}
               
    interface ITransactionDatabase with
        member _.GetStatusAsync(userId, transactionId) =
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

        member _.GetAsync(userId, transactionId) =
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
            
        member _.ExistsAsync(userId, transactionId) =
            connection
            |> Sql.query """
                SELECT EXISTS (
                    SELECT id FROM foxybalance_transactions
                    WHERE userid = @userId AND id = @id
                ) as transactionexists
                """
            |> Sql.parameters [
                "userId", Sql.int userId
                "id", Sql.int64 transactionId
            ]
            |> Sql.executeRowAsync (fun read -> read.bool "transactionexists")
            
        member _.BulkCreateAsync(userId, partialTransactions) =
            task {
                if List.isEmpty partialTransactions then
                    return 0
                else
                    // Collect all ImportIds from the transactions to check for existing records
                    let importIdsToCheck =
                        partialTransactions
                        |> List.choose (fun t -> t.ImportId)
                        |> List.distinct

                    // Query for existing transactions with these ImportIds
                    let! existingImportIds =
                        if List.isEmpty importIdsToCheck then
                            task { return Set.empty }
                        else
                            task {
                                let! existingIds =
                                    connection
                                    |> Sql.query """
                                        SELECT DISTINCT importid
                                        FROM foxybalance_transactions
                                        WHERE userid = @userId AND importid = ANY(@importIds)
                                    """
                                    |> Sql.parameters [
                                        "userId", Sql.int userId
                                        "importIds", Sql.stringArray (Array.ofList importIdsToCheck)
                                    ]
                                    |> Sql.executeAsync (fun read -> read.string "importid")
                                return Set.ofList existingIds
                            }

                    // Filter out transactions that already exist
                    let transactionsToImport =
                        partialTransactions
                        |> List.filter (fun t ->
                            match t.ImportId with
                            | Some importId -> not (Set.contains importId existingImportIds)
                            | None -> true // Always import transactions without an ImportId
                        )

                    if List.isEmpty transactionsToImport then
                        return 0
                    else
                        use conn = new NpgsqlConnection(options.ConnectionString)
                        do! conn.OpenAsync()

                        let copyCommand = """
                            COPY foxybalance_transactions (
                                userid,
                                datecreated,
                                name,
                                amount,
                                type,
                                recurring,
                                checknumber,
                                status,
                                datecleared,
                                importid
                            ) FROM STDIN (FORMAT BINARY)
                        """

                        use! writer = conn.BeginBinaryImportAsync(copyCommand)

                        for transaction in transactionsToImport do
                            do! writer.StartRowAsync()
                            // userid
                            do! writer.WriteAsync(userId, NpgsqlTypes.NpgsqlDbType.Integer)
                            // datecreated
                            do! writer.WriteAsync(transaction.DateCreated.ToUniversalTime(), NpgsqlTypes.NpgsqlDbType.TimestampTz)
                            // name
                            do! writer.WriteAsync(transaction.Name, NpgsqlTypes.NpgsqlDbType.Text)
                            // amount
                            do! writer.WriteAsync(transaction.Amount, NpgsqlTypes.NpgsqlDbType.Numeric)

                            // type, recurring, checknumber
                            match transaction.Type with
                            | Check check ->
                                do! writer.WriteAsync("Check", NpgsqlTypes.NpgsqlDbType.Text)
                                do! writer.WriteAsync(false, NpgsqlTypes.NpgsqlDbType.Boolean)
                                do! writer.WriteAsync(check.CheckNumber, NpgsqlTypes.NpgsqlDbType.Text)
                            | Bill bill ->
                                do! writer.WriteAsync("Bill", NpgsqlTypes.NpgsqlDbType.Text)
                                do! writer.WriteAsync(bill.Recurring, NpgsqlTypes.NpgsqlDbType.Boolean)
                                do! writer.WriteAsync(DBNull.Value, NpgsqlTypes.NpgsqlDbType.Text)
                            | Debit ->
                                do! writer.WriteAsync("Debit", NpgsqlTypes.NpgsqlDbType.Text)
                                do! writer.WriteAsync(false, NpgsqlTypes.NpgsqlDbType.Boolean)
                                do! writer.WriteAsync(DBNull.Value, NpgsqlTypes.NpgsqlDbType.Text)
                            | Credit ->
                                do! writer.WriteAsync("Credit", NpgsqlTypes.NpgsqlDbType.Text)
                                do! writer.WriteAsync(false, NpgsqlTypes.NpgsqlDbType.Boolean)
                                do! writer.WriteAsync(DBNull.Value, NpgsqlTypes.NpgsqlDbType.Text)

                            // status, datecleared
                            match transaction.Status with
                            | Pending ->
                                do! writer.WriteAsync("Pending", NpgsqlTypes.NpgsqlDbType.Text)
                                do! writer.WriteAsync(DBNull.Value, NpgsqlTypes.NpgsqlDbType.TimestampTz)
                            | Cleared date ->
                                do! writer.WriteAsync("Cleared", NpgsqlTypes.NpgsqlDbType.Text)
                                do! writer.WriteAsync(date.ToUniversalTime(), NpgsqlTypes.NpgsqlDbType.TimestampTz)

                            // importid
                            match transaction.ImportId with
                            | Some importId ->
                                do! writer.WriteAsync(importId, NpgsqlTypes.NpgsqlDbType.Varchar)
                            | None ->
                                do! writer.WriteAsync(DBNull.Value, NpgsqlTypes.NpgsqlDbType.Varchar)

                        let! rowsImported = writer.CompleteAsync()
                        return int rowsImported
            }

        member _.CreateAsync(userId, transaction) =
            let details = mapDetailsToSqlParams transaction.Type
            let status = mapStatusToSqlParams transaction.Status
            let importId =
                match transaction.ImportId with
                | Some id -> Sql.string id
                | None -> Sql.dbnull
            let data = [
                "userId", Sql.int userId
                "dateCreated", Sql.timestamptz (transaction.DateCreated.ToUniversalTime())
                "name", Sql.string transaction.Name
                "amount", Sql.decimal transaction.Amount
                "type", details.typeStr
                "checkNumber", details.checkNumber
                "recurring", details.recurring
                "status", status.statusStr
                "dateCleared", status.dateCleared
                "importId", importId
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
                    datecleared,
                    importid
                ) VALUES (
                    @userId,
                    @dateCreated,
                    @name,
                    @amount,
                    @type,
                    @recurring,
                    @checkNumber,
                    @status,
                    @dateCleared,
                    @importId
                )
                RETURNING id, datecreated
            """
            |> Sql.parameters data
            |> Sql.executeRowAsync (fun read ->
                {
                    Id = read.int64 "id"
                    Name = transaction.Name
                    Amount = transaction.Amount
                    DateCreated = read.datetimeOffset "datecreated"
                    Status = transaction.Status
                    Type = transaction.Type
                    ImportId = transaction.ImportId
                })
            
        member _.UpdateAsync(userId, transactionId, transaction) =
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
                RETURNING datecreated, importid
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
                    ImportId = read.stringOrNone "importid"
                })
            
        member _.ListAsync(userId, options) =
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
                match options.Status with
                | AllTransactions -> sql
                | _ -> sql + " AND status = @status"
            let sql =
              sql + $"""
              ORDER BY datecreated {direction}, id {direction}
              OFFSET @offset ROWS
              FETCH NEXT @limit ROWS ONLY
              """

            connection
            |> Sql.query sql
            |> Sql.parameters [
                "userId", Sql.int userId
                "offset", Sql.int options.Offset
                "limit", Sql.int options.Limit
                "status", statusFilterParameter options.Status
            ]
            |> Sql.executeAsync mapRowToTransaction
            |> Sql.map Seq.ofList
            
        member _.DeleteAsync(userId, transactionId) =
            connection
            |> Sql.query "DELETE FROM foxybalance_transactions WHERE userid = @userId AND id = @id"
            |> Sql.parameters [
                "userId", Sql.int userId
                "id", Sql.int64 transactionId
            ]
            |> Sql.executeNonQueryAsync
            |> Sql.ignore
            
        member _.CountAsync(userId, status) =
            let sql =
                """
                SELECT COUNT(id) as total
                FROM foxybalance_transactions
                WHERE userId = @userId
                """
            let sql =
                match status with
                | AllTransactions -> sql
                | _ -> sql + " AND status = @status"

            connection
            |> Sql.query sql
            |> Sql.parameters [
                "userId", Sql.int userId
                "status", statusFilterParameter status
            ]
            |> Sql.executeRowAsync (fun read -> int (read.int64 "total"))
            
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

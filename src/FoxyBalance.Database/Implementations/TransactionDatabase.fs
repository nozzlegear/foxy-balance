namespace FoxyBalance.Database

open System
open System.Data
open System.Threading.Tasks
open FoxyBalance.Database.Models
open FoxyBalance.Database.Interfaces
open Dapper

type TransactionDatabase(options : IDatabaseOptions) =
    let tableName = "FoxyBalance_Transactions"
    let connectionString = options.ConnectionString
    
    /// Parses the Id column from the data reader.
    let readIdColumn reader =
        match readColumn "Id" reader with
        | Error msg ->
            failwith msg
        | Ok x ->
            downcast x : int64 
    
    let mapRowToStatus (reader : IDataReader) : TransactionStatus =
        let statusCol = reader.GetOrdinal "Status"
        let clearedDateCol = reader.GetOrdinal "DateCleared"
        
        match reader.GetString statusCol with
        | "Pending" ->
            Pending
        | "Cleared" ->
            reader.GetDateTime clearedDateCol 
            |> DateTimeOffset
            |> Cleared
        | x ->
            failwithf """Unrecognized Status type "%s".""" x 
        
    let mapRowToDetails (reader : IDataReader) : TransactionType =
        let typeCol = reader.GetOrdinal "Type"
        let recurringCol = reader.GetOrdinal "Recurring"
        let checkNumberCol = reader.GetOrdinal "CheckNumber"
        
        match reader.GetString typeCol with
        | "Debit" ->
            Debit
        | "Credit" ->
            Credit 
        | "Bill" ->
            { Recurring = reader.GetBoolean recurringCol }
            |> Bill
        | "Check" ->
            { CheckNumber = reader.GetString checkNumberCol }
            |> Check
        | x ->
            failwithf """Unrecognized transaction type "%s".""" x 
        
    let statusFilter = function
        | AllTransactions ->
            ""
        | PendingTransactions ->
            " AND [Status] = 'Pending' "
        | ClearedTransactions ->
            " AND [Status] = 'Cleared' "
                    
    let mapRowToTransaction (reader : IDataReader) : Transaction =
        let idCol = reader.GetOrdinal "Id"
        let nameCol = reader.GetOrdinal "Name"
        let amountCol = reader.GetOrdinal "Amount"
        let dateCreatedCol = reader.GetOrdinal "DateCreated"
        
        { Id = reader.GetInt64 idCol
          Name = reader.GetString nameCol
          Amount = reader.GetDecimal amountCol
          DateCreated = reader.GetDateTime dateCreatedCol |> DateTimeOffset
          Status = mapRowToStatus reader
          Type = mapRowToDetails reader }
        
    let mapRowsToTransactions (reader : IDataReader) : Transaction seq =
        let output = [ while reader.Read() do yield mapRowToTransaction reader ]
        List.toSeq output
    
    let mapDetailsToSqlParams details =
        match details with
        | Check check ->
            {| typeStr = ParamValue.String "Check"
               checkNumber = ParamValue.String check.CheckNumber
               recurring = ParamValue.Bool false |}
        | Bill bill ->
            {| typeStr = ParamValue.String "Bill"
               checkNumber = ParamValue.Null
               recurring = ParamValue.Bool bill.Recurring |}
        | Debit ->
            {| typeStr = ParamValue.String "Debit"
               checkNumber = ParamValue.Null
               recurring = ParamValue.Bool false |}
        | Credit ->
            {| typeStr = ParamValue.String "Credit"
               checkNumber = ParamValue.Null
               recurring = ParamValue.Bool false |}
               
    let mapStatusToSqlParams status =
        match status with
        | Pending ->
            {| statusStr = ParamValue.String "Pending"
               dateCleared = ParamValue.Null |}
        | Cleared date -> 
            {| statusStr = ParamValue.String "Cleared"
               dateCleared = ParamValue.DateTimeOffset date |}
               
    interface ITransactionDatabase with
        member x.GetStatusAsync userId transactionId =
            let sql =
                sprintf """
                SELECT [Status], [DateCleared]
                FROM %s
                WHERE [UserId] = @userId AND [Id] = @id
                """ tableName
            let data = dict [
                "userId" => ParamValue.Int userId
                "id" => ParamValue.Long transactionId 
            ]
            
            withConnection connectionString (fun conn -> task {
                let! reader = conn.ExecuteReaderAsync(sql, data)
                
                if not <| reader.Read() then
                    failwithf "No transaction with ID %i and User ID %i" transactionId userId 
                
                return mapRowToStatus reader
            })

        member x.GetAsync userId transactionId =
            let sql =
                sprintf """
                SELECT * FROM %s WHERE [UserId] = @userId AND [Id] = @id
                """ tableName
            let data = dict [
                "userId" => ParamValue.Int userId
                "id" => ParamValue.Long transactionId 
            ]
            
            withConnection connectionString (fun conn -> task {
                let! result = conn.ExecuteReaderAsync(sql, data)
                return mapRowsToTransactions result |> Seq.tryExactlyOne
            })
            
        member x.ExistsAsync userId transactionId =
            let sql =
                sprintf """
                SELECT CASE WHEN EXISTS (
                    SELECT Id FROM %s WHERE [UserId] = @userId AND [Id] = @id
                )
                THEN CAST(1 AS BIT)
                ELSE CAST(0 AS BIT) END
                """ tableName
            let data = dict [
                "userId" => ParamValue.Int userId
                "id" => ParamValue.Long transactionId
            ]
            
            withConnection connectionString (fun conn -> conn.ExecuteScalarAsync<bool>(sql, data))
            
        member x.CreateAsync userId transaction =
            let sql =
                sprintf """
                INSERT INTO %s (
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
                """ tableName
            let details = mapDetailsToSqlParams transaction.Type
            let status = mapStatusToSqlParams transaction.Status
            let data = dict [
                "userId" => ParamValue.Int userId
                "dateCreated" => ParamValue.DateTimeOffset transaction.DateCreated
                "name" => ParamValue.String transaction.Name
                "amount" => ParamValue.Decimal transaction.Amount
                "type" => details.typeStr
                "checkNumber" => details.checkNumber
                "recurring" => details.recurring
                "status" => status.statusStr
                "dateCleared" => status.dateCleared
            ]
            
            withConnection connectionString (fun conn -> task {
                let! reader = conn.ExecuteReaderAsync(sql, data)
                
                if not (reader.Read()) then
                    failwith "Output for transaction insert operation contained no data, cannot read new transaction ID."
                    
                let transaction : Transaction =
                    { Id = readIdColumn reader
                      Name = transaction.Name
                      Amount = transaction.Amount
                      DateCreated = transaction.DateCreated
                      Status = transaction.Status
                      Type = transaction.Type }
                return transaction
            })
            
        member x.UpdateAsync userId transactionId transaction =
            let sql =
                sprintf """
                UPDATE %s
                SET [Name] = @name,
                [Amount] = @amount,
                [Type] = @type,
                [CheckNumber] = @checkNumber,
                [Recurring] = @recurring,
                [Status] = @status,
                [DateCleared] = @dateCleared
                OUTPUT INSERTED.DateCreated
                WHERE [UserId] = @userId AND [Id] = @id
                """ tableName
            let details = mapDetailsToSqlParams transaction.Type
            let status = mapStatusToSqlParams transaction.Status
            let data = dict [
                "userId" => ParamValue.Int userId
                "id" => ParamValue.Long transactionId
                "name" => ParamValue.String transaction.Name
                "amount" => ParamValue.Decimal transaction.Amount
                "type" => details.typeStr
                "checkNumber" => details.checkNumber
                "recurring" => details.recurring
                "status" => status.statusStr
                "dateCleared" => status.dateCleared
            ]
            
            withConnection connectionString (fun conn -> task {
                let! reader = conn.ExecuteReaderAsync(sql, data)
                
                if not (reader.Read()) then
                    failwith "Output for transaction update operation contained no data, cannot read transaction's DateCreated."
                
                let transaction : Transaction =
                    { Id = transactionId
                      Name = transaction.Name
                      Amount = transaction.Amount
                      DateCreated =
                            match readColumn "DateCreated" reader with
                            | Error msg ->
                                failwithf "Failed to read updated transaction's DateCreated column: %s" msg
                            | Ok x ->
                                (downcast x : DateTime)
                                |> DateTimeOffset
                      Status = transaction.Status
                      Type = transaction.Type }
                return transaction
            })
            
        member x.ListAsync userId options =
            let direction =
                match options.Order with
                | Ascending -> "ASC"
                | Descending -> "DESC"
            let whereClause =
                statusFilter options.Status
                |> sprintf "[UserId] = @userId %s"
            let sql =
                sprintf """
                SELECT * FROM %s
                WHERE %s
                ORDER BY [DateCreated] %s, [Id] %s
                OFFSET @offset ROWS
                FETCH NEXT @limit ROWS ONLY
                """ tableName whereClause direction direction
            let data = dict [
                "userId" => ParamValue.Int userId
                "offset" => ParamValue.Int options.Offset
                "limit" => ParamValue.Int options.Limit
            ]
            
            withConnection connectionString (fun conn -> task {
                let! reader = conn.ExecuteReaderAsync(sql, data)
                return mapRowsToTransactions reader 
            })
            
        member x.DeleteAsync userId transactionId =
            let sql =
                sprintf """
                DELETE FROM %s WHERE [UserId] = @userId AND [Id] = @id
                """ tableName
            let data = dict [
                "userId" => ParamValue.Int userId
                "id" => ParamValue.Long transactionId
            ]
            
            withConnection connectionString (fun conn -> task {
                let! _ = conn.ExecuteAsync(sql, data)
                ()  
            }) :> Task
            
        member x.CountAsync userId status =
            let sql =
                let whereClause =
                    statusFilter status
                    |> sprintf "[UserId] = @userId %s"
                sprintf """
                SELECT COUNT(Id)
                FROM %s
                WHERE %s
                """ tableName whereClause
            let data = dict [ "userId" => ParamValue.Int userId ]
            
            withConnection connectionString (fun conn -> conn.ExecuteScalarAsync<int>(sql, data))
            
        member x.SumAsync userId =
            let sql =
                sprintf """
                SELECT
                    SUM(CASE WHEN ([Status] = 'Cleared' AND [Type] <> 'Credit') THEN [Amount] ELSE 0 END) as ClearedDebit,
                    SUM(CASE WHEN ([Status] = 'Cleared' AND [Type] =  'Credit') THEN [Amount] ELSE 0 END) as ClearedCredit,
                    SUM(CASE WHEN ([Type] <> 'Credit') THEN [Amount] ELSE 0 END) as TotalDebit,
                    SUM(CASE WHEN ([Type] =  'Credit') THEN [Amount] ELSE 0 END) as TotalCredit
                FROM %s
                WHERE [UserId] = @userId
                """ tableName
            let data = dict [ "userId" => ParamValue.Int userId ]
            
            withConnection connectionString (fun conn -> task {
                let! reader = conn.ExecuteReaderAsync(sql, data)
                
                if not (reader.Read()) then
                    failwith "Output for transaction sum operation contained no data, cannot read sums."
                    
                let output: TransactionSum =
                    let readToDecimal columnName =
                        match Utils.readColumn columnName reader with
                        | Error msg ->
                            failwithf "Unable to read sum column %s: %s" columnName msg
                        | Ok x ->
                            // A SQL Sum operation returns null if there are no records matched.
                            // Default to 0 when that happens.
                            match x with
                            | :? System.DBNull ->
                                0.0M
                            | :? System.Decimal as x ->
                                x
                            | x ->
                                failwithf "Unexpected sum column value type %s" (x.GetType().Name)
                                
                    let totalCredit = readToDecimal "TotalCredit"
                    let totalDebit = readToDecimal "TotalDebit"
                    let clearedDebit = readToDecimal "ClearedDebit"
                    let clearedCredit = readToDecimal "ClearedCredit"
                    let pendingDebit = totalDebit - clearedDebit
                    let pendingCredit = totalCredit - clearedCredit
                    
                    { Sum = totalCredit - totalDebit
                      PendingSum = pendingCredit - pendingDebit
                      ClearedSum = clearedCredit - clearedDebit
                      ClearedDebitSum = clearedDebit
                      ClearedCreditSum = clearedCredit
                      PendingDebitSum = pendingDebit
                      PendingCreditSum = pendingCredit }
                    
                return output
            })

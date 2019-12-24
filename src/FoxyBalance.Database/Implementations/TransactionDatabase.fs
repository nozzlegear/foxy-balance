﻿namespace FoxyBalance.Database

open System
open System.Collections.Generic
open System.Data
open System.Threading.Tasks
open FSharp.Control.Tasks.V2.ContextInsensitive
open FoxyBalance.Database.Models
open FoxyBalance.Database.Interfaces
open Dapper

type TransactionDatabase(options : IDatabaseOptions) =
    let tableName = "FoxyBalance_Transactions"
    let connectionString = options.ConnectionString
    
    let mapRowToStatus (reader : IDataReader) : TransactionStatus =
        let statusCol = reader.GetOrdinal "Status"
        let expectedChargeDateCol = reader.GetOrdinal "ExpectedChargeDate"
        let completedDateCol = reader.GetOrdinal "CompletedDate"
        
        match reader.GetString statusCol with
        | "Pending" ->
            Pending
        | "PendingWithExpectedChargeDate" ->
            reader.GetDateTime expectedChargeDateCol
            |> DateTimeOffset
            |> PendingWithExpectedChargeDate
        | "Completed" ->
            reader.GetDateTime completedDateCol
            |> DateTimeOffset
            |> Completed
        | x ->
            failwithf """Unrecognized Status type "%s".""" x 
        
    let mapRowToDetails (reader : IDataReader) : TransactionDetails =
        let typeCol = reader.GetOrdinal "Type"
        let recurringCol = reader.GetOrdinal "Recurring"
        let checkNumberCol = reader.GetOrdinal "CheckNumber"
        
        match reader.GetString typeCol with
        | "Generic" ->
            NoDetails
        | "Bill" ->
            { Recurring = reader.GetBoolean recurringCol }
            |> BillDetails
        | "Check" ->
            { CheckNumber = reader.GetString checkNumberCol }
            |> CheckDetails
        | x ->
            failwithf """Unrecognized transaction type "%s".""" x 
        
    let mapRowToTransaction (reader : IDataReader) : Transaction =
        let idCol = reader.GetOrdinal "Id"
        let nameCol = reader.GetOrdinal "Name"
        let amountCol = reader.GetOrdinal "Amount"
        let dateCreatedCol = reader.GetOrdinal "DateCreated"
        
        { Id = reader.GetInt32 idCol
          Name = reader.GetString nameCol
          Amount = reader.GetDecimal amountCol
          DateCreated = reader.GetDateTime dateCreatedCol |> DateTimeOffset
          Status = mapRowToStatus reader
          Details = mapRowToDetails reader }
        
    let mapRowsToTransactions (reader : IDataReader) : Transaction seq =
        seq { while reader.Read() do yield mapRowToTransaction reader }
    
    let mapDetailsToSqlParams details =
        match details with
        | CheckDetails check ->
            {| typeStr = ParamValue.String "Check"
               checkNumber = ParamValue.String check.CheckNumber
               recurring = ParamValue.Bool false |}
        | BillDetails bill ->
            {| typeStr = ParamValue.String "Bill"
               checkNumber = ParamValue.Null
               recurring = ParamValue.Bool bill.Recurring |}
        | NoDetails ->
            {| typeStr = ParamValue.String "Generic"
               checkNumber = ParamValue.Null
               recurring = ParamValue.Bool false |}
               
    let mapStatusToSqlParams status =
        match status with
        | Pending ->
            {| statusStr = ParamValue.String "Pending"
               expectedChargeDate = ParamValue.Null
               completedDate = ParamValue.Null |}
        | PendingWithExpectedChargeDate date ->
            {| statusStr = ParamValue.String "PendingWithExpectedChargeDate"
               expectedChargeDate = ParamValue.DateTimeOffset date
               completedDate = ParamValue.Null |}
        | Completed date -> 
            {| statusStr = ParamValue.String "Completed"
               expectedChargeDate = ParamValue.Null
               completedDate = ParamValue.DateTimeOffset date |}
               
    interface ITransactionDatabase with
        member x.GetStatusAsync userId transactionId =
            let sql =
                sprintf """
                SELECT [Status], [ExpectedChargeDate], [CompletedDate]
                FROM %s
                WHERE [UserId] = @userId AND [Id] = @id
                """ tableName
            let data = dict [
                "userId" => ParamValue.Int userId
                "id" => ParamValue.Int transactionId 
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
                "id" => ParamValue.Int transactionId 
            ]
            
            withConnection connectionString (fun conn -> task {
                let! result = conn.ExecuteReaderAsync(sql, data)
                return mapRowsToTransactions result |> Seq.exactlyOne
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
                "id" => ParamValue.Int transactionId
            ]
            
            withConnection connectionString (fun conn -> conn.ExecuteScalarAsync<bool>(sql, data))
            
        member x.CreateAsync userId transaction =
            let sql =
                sprintf """
                INSERT INTO %s (
                    UserId,
                    Name,
                    Amount,
                    Type,
                    Recurring,
                    CheckNumber,
                    Status,
                    ExpectedChargeDate,
                    CompletedDate,
                ) OUTPUT INSERTED.Id VALUES(
                    @userId,
                    @dateCreated,
                    @name,
                    @amount,
                    @type,
                    @recurring,
                    @checkNumber,
                    @status,
                    @expectedChargeDate,
                    @completedDate
                )
                """ tableName
            let details = mapDetailsToSqlParams transaction.Details
            let status = mapStatusToSqlParams transaction.Status
            let dateCreated = System.DateTimeOffset.UtcNow
            let data = [
                "userId" => ParamValue.Int userId
                "dateCreated" => ParamValue.DateTimeOffset dateCreated
                "name" => ParamValue.String transaction.Name
                "amount" => ParamValue.Decimal transaction.Amount
                "type" => details.typeStr
                "checkNumber" => details.checkNumber
                "recurring" => details.recurring
                "status" => status.statusStr
                "expectedChargeDate" => status.expectedChargeDate
                "completedDate" => status.completedDate
            ]
            
            withConnection connectionString (fun conn -> task {
                let! result = conn.QuerySingleAsync<IDictionary<string, int>>(sql, data)
                let transaction : Transaction =
                    { Id = match result.TryGetValue "Id" with
                           | true, id -> id
                           | false, _ -> failwith "Failed to read new transaction's ID." 
                      Name = transaction.Name
                      Amount = transaction.Amount
                      DateCreated = dateCreated
                      Status = transaction.Status
                      Details = transaction.Details }
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
                [ExpectedChargeDate] = @expectedChargeDate,
                [CompletedDate] = @completedDate
                OUTPUT INSERTED.DateCreated
                WHERE [UserId] = @userId AND [Id] = @id
                """ tableName
            let details = mapDetailsToSqlParams transaction.Details
            let status = mapStatusToSqlParams transaction.Status
            let data = [
                "userId" => ParamValue.Int userId
                "id" => ParamValue.Int transactionId
                "name" => ParamValue.String transaction.Name
                "amount" => ParamValue.Decimal transaction.Amount
                "type" => details.typeStr
                "checkNumber" => details.checkNumber
                "recurring" => details.recurring
                "status" => status.statusStr
                "expectedChargeDate" => status.expectedChargeDate
                "completedDate" => status.completedDate
            ]
            
            withConnection connectionString (fun conn -> task {
                let! result = conn.QuerySingleAsync<IDictionary<string, DateTime>>(sql, data)
                let transaction : Transaction =
                    { Id = transactionId
                      Name = transaction.Name
                      Amount = transaction.Amount
                      DateCreated = match result.TryGetValue "DateCreated" with
                                    | true, date -> DateTimeOffset date
                                    | false, _ -> failwith "Failed to read updated transaction's DateCreated column."
                      Status = transaction.Status
                      Details = transaction.Details }
                return transaction
            })
            
        member x.ListAsync userId options =
            let sql =
                sprintf """
                SELECT *
                FROM %s
                WHERE [UserId] = @userId
                ORDER BY [Id] @direction
                OFFSET @offset ROWS
                FETCH NEXT @limit ROWS ONLY
                """ tableName
            let data = dict [
                "userId" => ParamValue.Int userId
                "offset" => ParamValue.Int options.Offset
                "limit" => ParamValue.Int options.Limit
                "direction" =>
                    (match options.Order with
                     | Ascending -> "ASC"
                     | Descending -> "DESC"
                     |> ParamValue.String)
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
                "id" => ParamValue.Int transactionId
            ]
            
            withConnection connectionString (fun conn -> task {
                let! _ = conn.ExecuteAsync(sql, data)
                ()  
            }) :> Task
namespace FoxyBalance.Database

open System
open System.Data
open FSharp.Control.Tasks.V2.ContextInsensitive
open FoxyBalance.Database.Models
open FoxyBalance.Database.Interfaces
open Dapper

type TransactionDatabase(connectionString : string) =
    let tableName = "FoxyBalance_Transactions"
    
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
        
        match reader.GetString typeCol with
        | "Generic" ->
            None
        | "Bill" ->
            { Recurring = reader.GetOrdinal "Recurring" |> reader.GetBoolean }
            |> BillDetails
        | "Check" ->
            { CheckNumber = reader.GetOrdinal "CheckNumber" |> reader.GetString }
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
            failwith "not implemented"
            
        member x.ListAsync userId limit offset =
            failwith "not implemented"
            
        member x.DeleteAsync userId transactionId =
            failwith "not implemented"

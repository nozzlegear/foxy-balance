namespace FoxyBalance.Database

open System
open System.Collections.Generic
open System.Data
open Dapper
open FSharp.Control.Tasks.V2.ContextInsensitive
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models

type UserDatabase(connectionString : string) =
    let tableName = "FoxyBalance_Users"
    
    /// Converts a UserIdentifier to a string * obj tuple, where the string is the SQL column name and the obj is the value
    let toSelector = function
        | Id id -> "Id", ParamValue.Int id
        | Email email -> "EmailAddress", ParamValue.String email
    
    interface IUserDatabase with
        member x.CreateAsync partialUser =
            let sql =
                sprintf """
                INSERT INTO %s (
                    EmailAddress,
                    HashedPassword,
                    DateCreated
                )
                OUTPUT INSERTED.Id
                VALUES (
                    @emailAddress,
                    @hashedPassword,
                    @dateCreated
                )
                """
            let dateCreated = DateTimeOffset.UtcNow
            let data = dict [
                "emailAddress" => ParamValue.String partialUser.EmailAddress
                "hashedPassword" => ParamValue.String partialUser.HashedPassword
                "dateCreated" => ParamValue.DateTimeOffset dateCreated 
            ]
             
            withConnection connectionString (fun conn -> task {
                let! result = conn.QuerySingleAsync<Dictionary<string, int>>(sql tableName, data)
                let user : User =
                    { DateCreated = dateCreated
                      Id = dictionaryToMap result |> Map.find "Id"
                      EmailAddress = partialUser.EmailAddress
                      HashedPassword = partialUser.HashedPassword }
                
                return user 
            })

        member x.GetAsync userId =
            let read (reader : IDataReader) : User seq =
                let col name = reader.GetOrdinal name 
                seq {
                    while reader.Read() do
                        yield
                            { Id = col "Id" |> reader.GetInt32
                              EmailAddress = col "EmailAddress" |> reader.GetString
                              DateCreated =
                                  let dt = col "DateCreated" |> reader.GetDateTime
                                  DateTimeOffset(dt)
                              HashedPassword = col "HashedPassword" |> reader.GetString } }
            let columnName, selector = toSelector userId
            let sql =
                sprintf """
                SELECT * FROM %s WHERE [%s] = @selector
                """ tableName columnName
            let data = dict [ "selector" => selector ]
                
            withConnection connectionString (fun conn -> task {
                use! reader = conn.ExecuteReaderAsync(sql, data)
                return read reader |> Seq.exactlyOne
            })

        member x.ExistsAsync userId =
            let columnName, selector = toSelector userId 
            let sql =
                sprintf """
                SELECT CASE WHEN EXISTS (
                    SELECT Id FROM %s WHERE [%s] = @selector
                )
                THEN CAST(1 AS BIT)
                ELSE CAST(0 AS BIT) END
                """ tableName columnName
            let data = dict [ "selector" => selector ]
            
            withConnection connectionString (fun conn -> conn.ExecuteScalarAsync<bool>(sql, data))

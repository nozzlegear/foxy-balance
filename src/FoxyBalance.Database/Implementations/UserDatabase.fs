namespace FoxyBalance.Database

open System
open System.Collections.Generic
open Dapper
open FSharp.Control.Tasks.V2.ContextInsensitive
open FoxyBalance.Database.Interfaces
open Models

type UserDatabase(connectionString : string) =
    let tableName = "FoxyBalance_User"
    
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
                "emailAddress" => partialUser.EmailAddress
                "hashedPassword" => partialUser.HashedPassword
                "dateCreated" => dateCreated 
            ]
             
            withConnection connectionString (fun conn -> task {
                let! result = conn.QuerySingleAsync<Dictionary<string, int>>(sql tableName, data)
                let m = dictionaryToMap result
                let user : User =
                    { DateCreated = dateCreated
                      Id = dictionaryToMap result |> Map.find "Id"
                      EmailAddress = partialUser.EmailAddress
                      HashedPassword = partialUser.HashedPassword }
                
                return user 
            })

        member x.GetAsync userId =
            
            failwith "not implemented"

        member x.ExistsAsync userId =
            failwith "not implemented"
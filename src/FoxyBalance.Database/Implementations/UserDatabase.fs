namespace FoxyBalance.Database

open System
open System.Data
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open DustyTables

type UserDatabase(options : IDatabaseOptions) =
    let connection =
        Sql.connect options.ConnectionString
        |> Sql.timeout 90
    
    interface IUserDatabase with
        member x.CreateAsync partialUser =
            let dateCreated = DateTimeOffset.UtcNow
            
            connection
            |> Sql.query """
                INSERT INTO FoxyBalance_Users (
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
            |> Sql.parameters [
                "emailAddress", Sql.string partialUser.EmailAddress
                "hashedPassword", Sql.string partialUser.HashedPassword
                "dateCreated", Sql.dateTimeOffset dateCreated 
            ]
            |> Sql.executeRowAsync (fun read ->
                {
                    DateCreated = dateCreated
                    Id = read.int "Id"
                    EmailAddress = partialUser.EmailAddress
                    HashedPassword = partialUser.HashedPassword
                })

        member _.GetAsync userId =
            let sql, selector =
                let format column = $"SELECT * FROM [FoxyBalance_Users] WHERE [{column}] = @selector"
                match userId with
                | Id id -> format "Id", Sql.int id
                | Email email -> format "EmailAddress", Sql.string email
                
            connection
            |> Sql.query sql
            |> Sql.parameters [
                "selector", selector
            ]
            |> Sql.executeAsync (fun read ->
                {
                    Id = read.int "Id"
                    EmailAddress = read.string "EmailAddress"
                    DateCreated = read.dateTime "DateCreated" |> DateTimeOffset
                    HashedPassword = read.string "HashedPassword"
                })
            |> Sql.tryExactlyOne

        member _.ExistsAsync userId =
            let sql, selector =
                let format column = $"""
                    SELECT CASE WHEN EXISTS (
                        SELECT Id FROM FoxyBalance_Users WHERE [{column}] = @selector
                    )
                    THEN CAST(1 AS BIT)
                    ELSE CAST(0 AS BIT)
                    END
                """
                match userId with
                | Id id -> format "Id", Sql.int id
                | Email email -> format "EmailAddress", Sql.string email
            
            connection
            |> Sql.query sql
            |> Sql.parameters [ "selector", selector ]
            |> Sql.executeRowAsync (fun read -> read.bool 0)

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
                INSERT INTO foxybalance_users (
                    emailaddress,
                    hashedpassword,
                    datecreated
                )
                VALUES (
                    @emailAddress,
                    @hashedPassword,
                    @dateCreated
                )
                RETURNING id
            """
            |> Sql.parameters [
                "emailAddress", Sql.string partialUser.EmailAddress
                "hashedPassword", Sql.string partialUser.HashedPassword
                "dateCreated", Sql.dateTimeOffset dateCreated
            ]
            |> Sql.executeRowAsync (fun read ->
                {
                    DateCreated = dateCreated
                    Id = read.int "id"
                    EmailAddress = partialUser.EmailAddress
                    HashedPassword = partialUser.HashedPassword
                })

        member _.GetAsync userId =
            let sql, selector =
                let format column = $"SELECT * FROM foxybalance_users WHERE {column} = @selector"
                match userId with
                | Id id -> format "id", Sql.int id
                | Email email -> format "emailaddress", Sql.string email

            connection
            |> Sql.query sql
            |> Sql.parameters [
                "selector", selector
            ]
            |> Sql.executeAsync (fun read ->
                {
                    Id = read.int "id"
                    EmailAddress = read.string "emailaddress"
                    DateCreated = read.dateTime "datecreated" |> DateTimeOffset
                    HashedPassword = read.string "hashedpassword"
                })
            |> Sql.tryExactlyOne

        member _.ExistsAsync userId =
            let sql, selector =
                let format column = $"""
                    SELECT EXISTS (
                        SELECT id FROM foxybalance_users WHERE {column} = @selector
                    )
                """
                match userId with
                | Id id -> format "id", Sql.int id
                | Email email -> format "emailaddress", Sql.string email

            connection
            |> Sql.query sql
            |> Sql.parameters [ "selector", selector ]
            |> Sql.executeRowAsync (fun read -> read.bool 0)

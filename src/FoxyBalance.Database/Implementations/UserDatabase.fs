namespace FoxyBalance.Database

open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open Npgsql.FSharp

type UserDatabase(options : IDatabaseOptions) =
    let connection =
        Sql.connect options.ConnectionString

    interface IUserDatabase with
        member x.CreateAsync partialUser =
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
                    NOW()
                )
                RETURNING id, datecreated
            """
            |> Sql.parameters [
                "emailAddress", Sql.string partialUser.EmailAddress
                "hashedPassword", Sql.string partialUser.HashedPassword
            ]
            |> Sql.executeRowAsync (fun read ->
                {
                    Id = read.int "id"
                    DateCreated = read.datetimeOffset "datecreated"
                    EmailAddress = partialUser.EmailAddress
                    HashedPassword = partialUser.HashedPassword
                })

        member _.GetAsync userId =
            let query, parameters =
                match userId with
                | Id id ->
                    "SELECT * FROM foxybalance_users WHERE id = @selector",
                    [ "@selector", Sql.int id ]
                | Email email ->
                    "SELECT * FROM foxybalance_users WHERE emailaddress = @selector",
                    [ "@selector", Sql.string email ]

            connection
            |> Sql.query query
            |> Sql.parameters parameters
            |> Sql.executeAsync (fun read ->
                {
                    Id = read.int "id"
                    EmailAddress = read.string "emailaddress"
                    DateCreated = read.datetimeOffset "datecreated"
                    HashedPassword = read.string "hashedpassword"
                })
            |> Sql.tryExactlyOne

        member _.ExistsAsync userId =
            let query, parameters =
                match userId with
                | Id id ->
                    "SELECT EXISTS (SELECT id FROM foxybalance_users WHERE id = @selector) AS user_exists",
                    [ "@selector", Sql.int id ]
                | Email email ->
                    "SELECT EXISTS (SELECT id FROM foxybalance_users WHERE emailaddress = @selector) AS user_exists",
                    [ "@selector", Sql.string email ]

            connection
            |> Sql.query query
            |> Sql.parameters parameters
            |> Sql.executeRowAsync (fun read -> read.bool "user_exists")

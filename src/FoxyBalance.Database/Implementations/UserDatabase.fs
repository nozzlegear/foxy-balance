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
            let columnParameter, selectorParameter =
                match userId with
                | Id id -> Sql.string "id", Sql.int id
                | Email email -> Sql.string "emailaddress", Sql.string email

            connection
            |> Sql.query
                """
                SELECT * FROM foxybalance_users
                WHERE @column = @selector
                """
            |> Sql.parameters [
                "@column", columnParameter
                "@selector", selectorParameter
            ]
            |> Sql.executeAsync (fun read ->
                {
                    Id = read.int "id"
                    EmailAddress = read.string "emailaddress"
                    DateCreated = read.datetimeOffset "datecreated"
                    HashedPassword = read.string "hashedpassword"
                })
            |> Sql.tryExactlyOne

        member _.ExistsAsync userId =
            let columnParameter, selectorParameter =
                match userId with
                | Id id -> Sql.string "id", Sql.int id
                | Email email -> Sql.string "emailaddress", Sql.string email

            connection
            |> Sql.query
                """
                SELECT EXISTS (
                    SELECT id FROM foxybalance_users WHERE @column = @selector
                ) as UserExists
                """
            |> Sql.parameters [
                "@column", columnParameter
                "@selector", selectorParameter
            ]
            |> Sql.executeRowAsync (fun read -> read.bool "UserExists")

namespace FoxyBalance.Database

open System
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open Npgsql.FSharp

type ApiKeyDatabase(options: IDatabaseOptions) =
    let connection = Sql.connect options.ConnectionString

    interface IApiKeyDatabase with
        member _.CreateAsync(userId, keyValue, secretHash, name) =
            connection
            |> Sql.query
                """
                INSERT INTO foxybalance_apikeys (userid, keyvalue, secrethash, name, datecreated, active)
                VALUES (@userId, @keyValue, @secretHash, @name, NOW(), true)
                RETURNING id
            """
            |> Sql.parameters
                [ "userId", Sql.int userId
                  "keyValue", Sql.string keyValue
                  "secretHash", Sql.string secretHash
                  "name", Sql.string name ]
            |> Sql.executeRowAsync (fun read -> read.int64 "id")

        member _.GetByKeyValueAsync(keyValue) =
            connection
            |> Sql.query
                """
                SELECT userid, id, secrethash
                FROM foxybalance_apikeys
                WHERE keyvalue = @keyValue AND active = true
            """
            |> Sql.parameters [ "keyValue", Sql.string keyValue ]
            |> Sql.executeAsync (fun read -> (read.int "userid", read.int64 "id", read.string "secrethash"))
            |> Sql.tryExactlyOne

        member _.ListForUserAsync(userId) =
            task {
                let! results =
                    connection
                    |> Sql.query
                        """
                        SELECT id, userid, keyvalue, name, datecreated, lastused, active
                        FROM foxybalance_apikeys
                        WHERE userid = @userId
                        ORDER BY datecreated DESC
                    """
                    |> Sql.parameters [ "userId", Sql.int userId ]
                    |> Sql.executeAsync (fun read ->
                        { Id = read.int64 "id"
                          UserId = read.int "userid"
                          KeyValue = read.string "keyvalue"
                          Name = read.string "name"
                          DateCreated = read.datetimeOffset "datecreated"
                          LastUsed = read.datetimeOffsetOrNone "lastused"
                          Active = read.bool "active" })

                return results :> ApiKeyInfo seq
            }

        member _.UpdateLastUsedAsync(keyId) =
            connection
            |> Sql.query
                """
                UPDATE foxybalance_apikeys
                SET lastused = NOW()
                WHERE id = @keyId
            """
            |> Sql.parameters [ "keyId", Sql.int64 keyId ]
            |> Sql.executeNonQueryAsync
            |> Sql.ignore

        member _.RevokeAsync(userId, keyId) =
            connection
            |> Sql.query
                """
                UPDATE foxybalance_apikeys
                SET active = false
                WHERE id = @keyId AND userid = @userId
            """
            |> Sql.parameters [ "keyId", Sql.int64 keyId; "userId", Sql.int userId ]
            |> Sql.executeNonQueryAsync
            |> Sql.ignore

        member _.DeleteAsync(userId, keyId) =
            connection
            |> Sql.query
                """
                DELETE FROM foxybalance_apikeys
                WHERE id = @keyId AND userid = @userId
            """
            |> Sql.parameters [ "keyId", Sql.int64 keyId; "userId", Sql.int userId ]
            |> Sql.executeNonQueryAsync
            |> Sql.ignore

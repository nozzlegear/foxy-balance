namespace FoxyBalance.Database

open System
open FoxyBalance.Database.Interfaces
open FoxyBalance.Database.Models
open Npgsql.FSharp

type RefreshTokenDatabase(options: IDatabaseOptions) =
    let connection = Sql.connect options.ConnectionString

    interface IRefreshTokenDatabase with
        member _.CreateAsync(userId, tokenHash, expiresAt) =
            connection
            |> Sql.query
                """
                INSERT INTO foxybalance_refreshtokens (userid, tokenhash, expiresat, used, datecreated)
                VALUES (@userId, @tokenHash, @expiresAt, false, NOW())
                RETURNING id
            """
            |> Sql.parameters
                [ "userId", Sql.int userId
                  "tokenHash", Sql.string tokenHash
                  "expiresAt", Sql.timestamptz expiresAt ]
            |> Sql.executeRowAsync (fun read -> read.int64 "id")

        member _.GetByHashAsync(tokenHash) =
            connection
            |> Sql.query
                """
                SELECT id, userid, expiresat, used, datecreated
                FROM foxybalance_refreshtokens
                WHERE tokenhash = @tokenHash
            """
            |> Sql.parameters [ "tokenHash", Sql.string tokenHash ]
            |> Sql.executeAsync (fun read ->
                { Id = read.int64 "id"
                  UserId = read.int "userid"
                  ExpiresAt = read.datetimeOffset "expiresat"
                  Used = read.bool "used"
                  DateCreated = read.datetimeOffset "datecreated" })
            |> Sql.tryExactlyOne

        member _.MarkAsUsedAsync(tokenId) =
            connection
            |> Sql.query
                """
                UPDATE foxybalance_refreshtokens
                SET used = true
                WHERE id = @tokenId
            """
            |> Sql.parameters [ "tokenId", Sql.int64 tokenId ]
            |> Sql.executeNonQueryAsync
            |> Sql.ignore

        member _.ConsumeRefreshTokenAsync(tokenHash) =
            connection
            |> Sql.query
                """
                UPDATE foxybalance_refreshtokens
                SET used = true
                WHERE tokenhash = @tokenHash
                  AND used = false
                  AND expiresat > NOW()
                RETURNING id, userid, expiresat, used, datecreated
            """
            |> Sql.parameters [ "tokenHash", Sql.string tokenHash ]
            |> Sql.executeAsync (fun read ->
                { Id = read.int64 "id"
                  UserId = read.int "userid"
                  ExpiresAt = read.datetimeOffset "expiresat"
                  Used = read.bool "used"
                  DateCreated = read.datetimeOffset "datecreated" })
            |> Sql.tryExactlyOne

        member _.DeleteExpiredAsync() =
            connection
            |> Sql.query
                """
                DELETE FROM foxybalance_refreshtokens
                WHERE expiresat < NOW() OR (used = true AND datecreated < NOW() - INTERVAL '7 days')
            """
            |> Sql.executeNonQueryAsync

        member _.DeleteAllForUserAsync(userId) =
            connection
            |> Sql.query
                """
                DELETE FROM foxybalance_refreshtokens
                WHERE userid = @userId
            """
            |> Sql.parameters [ "userId", Sql.int userId ]
            |> Sql.executeNonQueryAsync
            |> Sql.ignore

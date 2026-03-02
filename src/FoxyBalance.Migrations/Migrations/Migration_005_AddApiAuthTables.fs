namespace FoxyBalance.Migrations

open FluentMigrator

[<Migration(5L, "Add API key and refresh token tables for REST API authentication")>]
type Migration_005_AddApiAuthTables() =
    inherit Migration()

    override this.Up() =
        this.Execute.Sql("""
            -- API Keys table for storing API key credentials
            CREATE TABLE foxybalance_apikeys (
                id BIGSERIAL PRIMARY KEY,
                userid INT NOT NULL REFERENCES foxybalance_users(id) ON DELETE CASCADE,
                keyvalue VARCHAR(64) NOT NULL UNIQUE,
                secrethash TEXT NOT NULL,
                name VARCHAR(255) NOT NULL,
                datecreated TIMESTAMPTZ NOT NULL,
                lastused TIMESTAMPTZ,
                active BOOLEAN NOT NULL DEFAULT true
            );

            CREATE INDEX idx_apikeys_userid ON foxybalance_apikeys (userid);
            CREATE INDEX idx_apikeys_keyvalue ON foxybalance_apikeys (keyvalue) WHERE active = true;

            -- Refresh Tokens table for rolling token authentication
            CREATE TABLE foxybalance_refreshtokens (
                id BIGSERIAL PRIMARY KEY,
                userid INT NOT NULL REFERENCES foxybalance_users(id) ON DELETE CASCADE,
                tokenhash TEXT NOT NULL UNIQUE,
                expiresat TIMESTAMPTZ NOT NULL,
                used BOOLEAN NOT NULL DEFAULT false,
                datecreated TIMESTAMPTZ NOT NULL
            );

            CREATE INDEX idx_refreshtokens_tokenhash ON foxybalance_refreshtokens (tokenhash) WHERE used = false;
            CREATE INDEX idx_refreshtokens_expiresat ON foxybalance_refreshtokens (expiresat) WHERE used = false;
        """)

    override this.Down() =
        this.Delete.Table("foxybalance_refreshtokens") |> ignore
        this.Delete.Table("foxybalance_apikeys") |> ignore

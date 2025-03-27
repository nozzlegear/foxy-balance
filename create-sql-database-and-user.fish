#! /usr/bin/env fish

# sqlcmd comes from Homebrew's mssql-tools package. It can also be found
# inside /opt/mssql folder in any Sql Server container.
sqlcmd -U sa \
    -P "a-BAD_passw0rd" \
    -d "master" \
    -S "localhost,1433" \
    -i src/FoxyBalance.Migrations/Migrations/sql/CreateDatabases.sql

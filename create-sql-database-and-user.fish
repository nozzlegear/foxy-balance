#! /usr/bin/env fish

# psql doesn't support the admin password via CLI, so it needs to be set via PGPASSWORD env
set -x PGPASSWORD "$GENERIC_PSQL_PASSWORD"

# psql comes from Postgres or Homebrew's libpq package
psql -h localhost \
    -p 5432 \
    -U watchmaker_sa \
    -d postgres \
    -f src/FoxyBalance.Migrations/Migrations/sql/CreateDatabases.sql \
    -v "dbname=foxybalance" \
    -v "dbuser=foxybalance_app" \
    -v "dbpass=a-BAD_passw0rd"


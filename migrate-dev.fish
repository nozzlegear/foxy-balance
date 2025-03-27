#! /usr/bin/env fish

if not count $argv > /dev/null
    set -l scriptName (status -f)

    echo ""
    echo "Usage: $scriptName [up|down|to migration_number]"
    exit 1
end

# Assume target database is the container used in dev
set connectionString "Server=localhost,1433;Database=FoxyBalance;User Id=FoxyBalance_Server;Password=a-BAD_passw0rd"

dotnet run --project ./src/FoxyBalance.Migrator -- $argv -c "$connectionString"

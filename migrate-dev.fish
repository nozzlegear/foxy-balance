#! /usr/bin/env fish

if not count $argv > /dev/null
    set -l scriptName (status -f)

    echo ""
    echo "Usage: $scriptName [up|down|to migration_number]"
    return 1
end

# Assume target database is the one in dev
set appsettings_file (path resolve ./src/FoxyBalance.Server/appsettings.Development.json)

if ! test -f "$appsettings_file"
    set_color red
    echo "$appsettings_file does not exist, unable to read connection string."
    return 1
end

set connection_string (cat "$appsettings_file" | jq -c -r -e '.ConnectionStrings.Database')
or return 1

if test -z "$connection_string"
    set_color red
    echo ".ConnectionStrings.Database value in $appsettings_file is null or empty, unable to read connection string."
    return 1
end

dotnet run --project ./src/FoxyBalance.Migrator -- $argv -c "$connection_string"
or return 1

#! /usr/bin/env fish

# Set dev environment and start project
set -x ASPNETCORE_ENVIRONMENT "development"
dotnet watch --project src/FoxyBalance.Server/FoxyBalance.Server.fsproj run -c Debug

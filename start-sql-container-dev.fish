#! /usr/bin/env fish

set dbImage "mcr.microsoft.com/mssql/server:2022-latest"
set containerName "blackbox"

function pod
    if command -q podman
        podman $argv
    else if command -q docker
        docker $argv
    else
        set_color red
        "Could not find podman or docker, please install one of them and try again."
        return 1
    end
end

# Check if the container exists
if test (pod ps -a -f "name=$containerName" -q)
    echo "Starting database container..."
    pod start "$containerName"
    or return 1
else
    set_color red
    echo "Container $containerName does not exist. You must create the container first (use the restore-sql-blackbox.fish script in \$r/dotfiles)."
    return 1
end

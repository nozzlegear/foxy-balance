#! /usr/bin/env fish

set volumeLocation "$HOME/.local/volumes/foxy-balance/mssql"
set dbImage "mcr.microsoft.com/mssql/server:2017-latest-ubuntu"
set containerName "foxybalance_db_1"
set containerPort "7021"
set sqlPassword "a-BAD_passw0rd"
set useSudoForDocker

function isArm64
    # Note that functions in fish return exit codes, not boolean true/false
    if test (uname -m) = "arm64"
        true
    else
        false
    end
end

# Formats the database container's system user. If the host is arm64, this well set the user to root, which is required for the Azure SQL Edge db image.
function formatDbSystemUser
    if isArm64
        echo "-u=root"
    end
end

# If the user is on arm, change the db image to Azure SQL Edge, as the default mssql server does not currently support arm.
if isArm64
    set dbImage "mcr.microsoft.com/azure-sql-edge:1.0.5"
end

if test ! -d "$volumeLocation"
    mkdir -p "$volumeLocation"
end

# Figure out whether to use podman, docker or sudo docker to start containers
if command -q podman
    set USE_PODMAN 1
else 
    set USE_PODMAN 0

    # Check if the user can use Docker without sudo
    if docker ps &> /dev/null
        set USE_SUDO_FOR_DOCKER 0
    else if sudo docker ps &> /dev/null
        set USE_SUDO_FOR_DOCKER 1
    else
        printErr "'podman', 'docker ps' and 'sudo docker ps' commands failed to return a successful exit code. Are Podman or Docker configured properly? Do 'podman ps', 'docker ps' or 'sudo docker ps' work?"
        exit 1
    end
end

function pod 
    if test $USE_PODMAN -eq 1
        podman $argv
    else if test $USE_SUDO_FOR_DOCKER -eq 1
        sudo docker $argv
    else
        docker $argv
    end
end

# Check if the container exists
if test (pod ps -a -f "name=$containerName" -q)
    echo "Starting database container..."
    pod start "$containerName"
    or exit 1
else
    echo "Container $containerName does not exist, creating it..."
    echo "Using sql password $sqlPassword"
    pod run \
        -dit \
        --name "$containerName" \
        -e "SA_PASSWORD=$sqlPassword" \
        -e "ACCEPT_EULA=Y" \
        -p "$containerPort:1433" \
        -v "$volumeLocation:/var/opt/mssql" \
        (formatDbSystemUser) \
        "$dbImage"
    or exit 1
end 

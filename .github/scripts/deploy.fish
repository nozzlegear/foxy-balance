#! /usr/bin/env fish

set LOG_FILE "/var/log/deploy-foxy-balance.log"

function printErr -a msg
    set_color red
    echo "$msg" >&2
    set_color normal
end

function log -a msg
    set timestamp (date -u "+%F T%TZ")

    # Echo to the log file and to the console
    echo "[$timestamp]: $msg" >> "$LOG_FILE"
    echo "$msg"
end

# A function to format a list of secrets into `podman run` args
function formatSecrets 
    for secret in $argv
        echo "--secret=$secret"
    end
end

# Secrets used by the container. These secret names should match the names of the environment variables needed by the app. Double underscores correspond to a new section in dotnet config sections.
set CONTAINER_SECRETS_LIST "FoxyBalance_HashingKey" \
    "FoxyBalance_ConnectionStrings__SqlDatabase" \
    "FoxyBalance_Gumroad__ApplicationSecret" \
    "FoxyBalance_Gumroad__ApplicationId" \
    "FoxyBalance_Gumroad__AccessToken"

set CONTAINER_IMAGE "$argv[1]"
set CONTAINER_NAME "foxy_balance"
set CONTAINER_PORT_MAP "5002:3000"

if test -z "$CONTAINER_IMAGE"
    printErr "No image given, cannot deploy update."
    set_color yellow
    echo "Usage: ./script.fish example.azurecr.io/image:version"
    exit 1
end

if ! command -q podman
    printErr "`podman` command not found. Is podman installed? Does `podman ps` work? Does `command -v podman` work?"
    exit 1
end

# Update the images
log "Pulling container image from $CONTAINER_IMAGE..."
podman pull "$CONTAINER_IMAGE"
or exit 1

# Remove the existing container so it can be updated
if podman container exists "$CONTAINER_NAME"
    log "Removing container $CONTAINER_NAME..."
    podman stop "$CONTAINER_NAME" 
    and podman rm "$CONTAINER_NAME"
end

# Create the container, but don't start it.
log "Creating container $CONTAINER_NAME..."
podman create \
    --restart "unless-stopped" \
    --name "$CONTAINER_NAME" \
    --publish "$CONTAINER_PORT_MAP" \
    (formatSecrets $CONTAINER_SECRETS_LIST) \
    -it \
    "$CONTAINER_IMAGE"
or exit 1

# Start the container
log "Starting container $CONTAINER_NAME"
podman start "$CONTAINER_NAME"
or exit 1

log "Done!"

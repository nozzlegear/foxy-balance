set shell := ["pwsh", "-c"]
set script-interpreter := ["pwsh", "-c"]

repo := "ghcr.io/nozzlegear/foxy-balance"
controlSocket := "/tmp/ssh-control-foxy-balance"
ssh_opts := "-o StrictHostKeyChecking=yes -o SendEnv=no -o ControlMaster=auto -o ControlPath=" + controlSocket + " -o ControlPersist=30s"

# List available recipes
[private]
default:
    @just --list

# Generate quadlet unit files from pod.pkl.
# Outputs to `output_dir` (default: quadlet/output); override for CI: just generate <image> /tmp/quadlets
[script]
[group("release")]
generate image="ghcr.io/nozzlegear/foxy-balance:latest" output_dir="quadlet/output":
    New-Item -ItemType Directory -Force -Path "{{output_dir}}" | Out-Null
    pkl eval quadlet/pod.pkl -p 'appImageName={{image}}' -m "{{output_dir}}"

# Build the app container image locally
[group("release")]
build tag="latest" commit="":
    $commit = "{{ if commit != '' { commit } else {`git rev-parse head`} }}"
    podman build \
        -t "{{repo}}:{{tag}}" \
        -t "{{repo}}:latest" \
        --build-arg "RUN={{tag}}" \
        --build-arg "COMMIT=$commit" \
        .

# Print the manifest digest for a pushed image tag
[script]
[group("release")]
get-digest tag="latest":
    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        skopeo inspect --raw "docker://{{repo}}:{{tag}}" | Set-Content $tmp
        skopeo manifest-digest $tmp
    } finally {
        Remove-Item $tmp -ErrorAction SilentlyContinue
    }

# Deploys the generated quadlet files to the Systemd container folder on the host
[script]
[group("release")]
deploy-quadlets sshTarget quadletDir:
    $sshTarget = "{{sshTarget}}"
    $quadletDir = "{{quadletDir}}"

    try {
        scp {{ssh_opts}} $quadletDir "${sshTarget}:.config/containers/systemd/"
        Write-Output 'Done.'
    } finally {
        just _cleanup-ssh
    }

# Decrypt secrets.json and update Podman secrets on the SSH host if they have changed.
[script]
[group("release")]
deploy-secrets sshTarget secretFile:
    $secretFile = "{{secretFile}}"
    $sshTarget = "{{sshTarget}}"

    try {
        scp {{ssh_opts}} $secretFile "${sshTarget}:/tmp/appsettings.secrets.json"
        ssh {{ssh_opts}} $sshTarget 'podman secret rm foxybalance_secrets 2>/dev/null || true'
        ssh {{ssh_opts}} $sshTarget 'podman secret create foxybalance_secrets /tmp/appsettings.secrets.json'
        ssh {{ssh_opts}} $sshTarget 'set PG_USER (jq -r .Postgres_Username /tmp/appsettings.secrets.json); podman secret rm foxybalance_pg_username 2>/dev/null or true; printf "%s" "$PG_USER" | podman secret create foxybalance_pg_username -'
        ssh {{ssh_opts}} $sshTarget 'set PG_PASS (jq -r .Postgres_Password /tmp/appsettings.secrets.json); podman secret rm foxybalance_pg_password 2>/dev/null or true; printf "%s" "$PG_PASS" | podman secret create foxybalance_pg_password -'
        ssh {{ssh_opts}} $sshTarget 'rm /tmp/appsettings.secrets.json'
        Write-Output 'Done.'
    } finally {
        just _cleanup-ssh
    }

# Reload systemd quadlets and restart the app service on the SSH host.
[script]
[group("release")]
restart-systemd sshTarget:
    $sshTarget = "{{sshTarget}}"

    try {
        ssh {{ssh_opts}} $sshTarget `
            'systemctl --user daemon-reload && systemctl --user restart foxy-balance-app.service'
    } finally {
        just _cleanup-ssh
    }

[script]
[private]
[group("release")]
_cleanup-ssh:
    ssh -O exit -o "ControlPath={{controlSocket}}" $sshTarget 2>$null
    Remove-Item {{controlSocket}} -ErrorAction SilentlyContinue

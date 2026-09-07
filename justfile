set shell := ["pwsh", "-c"]
set script-interpreter := ["pwsh", "-c"]

repo := "ghcr.io/nozzlegear/foxy-balance"
controlSocket := "/tmp/ssh-control-foxy-balance"
ssh_opts := "-o StrictHostKeyChecking=yes -o SendEnv=no -o ControlMaster=auto -o ControlPath=" + controlSocket + " -o ControlPersist=30s"
rsync_opts := "-e 'ssh " + ssh_opts + "'"
quadletTmpDir := "/tmp/dijon-quadlet"

# List available recipes
[private]
default:
    @just --list

# Build the CLI as a self-contained trimmed single-file executable for the current platform.
[script]
[group("cli")]
build-cli:
    $rid = if ([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq "Arm64") { "osx-arm64" } else { "osx-x64" }
    dotnet publish src/FoxyBalance.CLI/FoxyBalance.CLI.fsproj -c Release -r $rid -o ./publish
    Write-Output "Built publish/foxy-balance"

# Install the built CLI to ~/.local/bin (must be in PATH).
[script]
[group("cli")]
install-cli:
    $dest = "$HOME/.local/bin"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item ./publish/foxy-balance "$dest/fb" -Force
    Write-Output "Installed to $dest/fb"

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
        skopeo inspect --raw "docker://{{repo}}:{{tag}}" | Set-Content -NoNewLine $tmp
        skopeo manifest-digest $tmp
        $exitCode = $LASTEXITCODE
    } finally {
        Remove-Item $tmp -ErrorAction SilentlyContinue
    }
    if ($exitCode -ne 0) { exit $exitCode }

# Deploys the generated quadlet files to the Systemd container folder on the host
[group("release")]
deploy-quadlets host quadletDir: && _cleanup-ssh
    @ssh {{ssh_opts}} "{{host}}" "mkdir -p .config/containers/systemd .config/systemd/user"

    @rsync {{rsync_opts}} \
        {{clean(quadletDir + "/*")}} \
        "{{host}}:.config/containers/systemd/"

# Decrypt secrets.json and update Podman secrets on the SSH host if they have changed.
[script]
[group("release")]
deploy-secrets sshTarget secretFile:
    $secretFile = "{{secretFile}}"
    $sshTarget = "{{sshTarget}}"

    try {
        scp {{ssh_opts}} $secretFile "${sshTarget}:/tmp/appsettings.secrets.json"
        rsync -e "ssh {{ssh_opts}}" "$secretFile" "${sshTarget}:/tmp/appsettings.secrets.json"

        # Create the full secrets file as a podman secret for the app container
        ssh {{ssh_opts}} $sshTarget 'podman secret rm foxybalance_secrets 2>/dev/null || true'
        ssh {{ssh_opts}} $sshTarget 'podman secret create foxybalance_secrets /tmp/appsettings.secrets.json'

        # Create individual podman secrets for PostgreSQL from the Postgres section
        ssh {{ssh_opts}} $sshTarget 'set PG_USER (jq -r .Postgres.Username /tmp/appsettings.secrets.json); podman secret rm foxybalance_pg_username 2>/dev/null; or true; printf "%s" "$PG_USER" | podman secret create foxybalance_pg_username -'
        ssh {{ssh_opts}} $sshTarget 'set PG_PASS (jq -r .Postgres.Password /tmp/appsettings.secrets.json); podman secret rm foxybalance_pg_password 2>/dev/null; or true; printf "%s" "$PG_PASS" | podman secret create foxybalance_pg_password -'

        # Clean up
        ssh {{ssh_opts}} $sshTarget 'rm /tmp/appsettings.secrets.json'
        $exitCode = $LASTEXITCODE
    } finally {
        just _cleanup-ssh
    }
    if ($exitCode -ne 0) { exit $exitCode }

# Reload systemd quadlets and restart the app service on the SSH host.
[group("release")]
restart-systemd host: && _cleanup-ssh
    @ssh {{ssh_opts}} "{{host}}" "systemctl --user daemon-reload && systemctl --user restart foxy-balance-app.service"

[script]
[private]
[group("release")]
_cleanup-ssh:
    ssh -O exit -o "ControlPath={{controlSocket}}" $sshTarget 2>$null
    Remove-Item {{controlSocket}} -ErrorAction SilentlyContinue

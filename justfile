set shell := ["pwsh", "-c"]
set script-interpreter := ["pwsh", "-c"]

repo := "ghcr.io/nozzlegear/foxy-balance"
controlSocket := "/tmp/ssh-control-foxy-balance"
ssh_opts := "-o StrictHostKeyChecking=yes -o SendEnv=no -o ControlMaster=auto -o ControlPath=" + controlSocket + " -o ControlPersist=30s"
rsync_opts := "-e 'ssh " + ssh_opts + "'"
secretsFile := "secrets.json"

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

# Decrypt secrets.json and update Podman secrets on the SSH host if they have changed.
[group("release")]
deploy-secrets host:
    @sops exec-file "{{secretsFile}}" 'just _deploy-decrypted-secrets {{host}} {}'

[script("fish")]
[group("release")]
_deploy-decrypted-secrets host decryptedSecretsFile: &&_cleanup-ssh
    # The decrypted file can only be read once by default (sops uses a fifo instead of a regular file)
    set -l secretContent (cat {{decryptedSecretsFile}})

    # Create the full secrets file as a podman secret for the app container
    echo -n $secretContent | ssh {{ssh_opts}} "{{host}}" podman secret create --replace foxybalance_secrets -

    # Create individual podman secrets for PostgreSQL from the Postgres section
    echo -n $secretContent | jq -r ".Postgres.Username" | ssh {{ssh_opts}} "{{host}}" podman secret create --replace foxybalance_pg_username -
    echo -n $secretContent | jq -r ".Postgres.Password" | ssh {{ssh_opts}} "{{host}}" podman secret create --replace foxybalance_pg_password -

[script("pwsh")]
_cleanup-ssh:
    ssh -O exit -o "ControlPath={{controlSocket}}" $sshTarget 2>$null
    Remove-Item {{controlSocket}} -ErrorAction SilentlyContinue

# Deploy quadlet files and Caddyfile to the host using Ansible.
# Usage: just deploy-ansible HOST USER QUADLET_DIR
# Example: just deploy-ansible user@host.com myuser /tmp/quadlets
[group("release")]
deploy-ansible host user quadletDir:
    ansible-playbook -i "{{host}}," -e "ansible_user={{user}}" -e "quadlet_src={{quadletDir}}" deploy/playbook.yml

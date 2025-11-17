# Podman Quadlet Configuration

This directory contains Podman quadlet files for running FoxyBalance using systemd.

## Files

- `foxy-balance.kube` - Main quadlet file that references the Kubernetes YAML
- `foxy-balance.network` - Network configuration for the pod
- `foxy-balance.volume` - Persistent volume for PostgreSQL data

## Prerequisites

1. Generate the pod.yaml file from the Pkl configuration:
   ```bash
   pkl eval quadlet/pod.pkl -o ~/sites-enabled/foxy-balance/pod.yaml
   ```

2. Create the secrets YAML file at ~/sites-enabled/foxy-balance/secrets.yaml with your secrets

## Installation

1. Copy the quadlet files to the systemd user directory:
   ```bash
   mkdir -p ~/.config/containers/systemd
   cp quadlet/*.{kube,network,volume} ~/.config/containers/systemd/
   ```

2. Reload systemd to detect the new quadlet files:
   ```bash
   systemctl --user daemon-reload
   ```

3. Start the service:
   ```bash
   systemctl --user start foxy-balance
   ```

4. Enable the service to start on boot:
   ```bash
   systemctl --user enable foxy-balance
   ```

## Usage

- Start: `systemctl --user start foxy-balance`
- Stop: `systemctl --user stop foxy-balance`
- Status: `systemctl --user status foxy-balance`
- Logs: `journalctl --user -u foxy-balance -f`
- Restart: `systemctl --user restart foxy-balance`

## Notes

- The pod.yaml file must exist at `~/sites-enabled/foxy-balance/pod.yaml` before starting the service
- Secrets must be properly configured in the secrets.yaml file
- The volume will persist data between container restarts
- Container image auto-update is enabled by default

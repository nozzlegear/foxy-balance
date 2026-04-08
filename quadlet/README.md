# Podman Quadlet Configuration

This directory contains Podman quadlet configuration for FoxyBalance. `pod.pkl` is the
source of truth — it generates native systemd unit files for both containers, the network,
and the volume.

## Files

- `pod.pkl` — generates `foxy-balance-app.container`, `foxy-balance-db.container`,
  `foxy-balance.network`, and `foxy-balance.volume`

## Prerequisites

Create the Podman secrets on the host:
```bash
# Full appsettings JSON (mounted at /run/secrets/appsettings.secrets.json in the app)
podman secret create foxybalance_secrets /path/to/appsettings.secrets.json

# Flat credentials for the Postgres container
echo -n "myuser"     | podman secret create foxybalance_pg_username -
echo -n "mypassword" | podman secret create foxybalance_pg_password -
```

The connection string in `appsettings.secrets.json` must use `Host=foxy-balance-db`
(the `ContainerName` of the DB container) rather than `localhost`.

## Installation

1. Generate the quadlet files:
   ```bash
   pkl eval quadlet/pod.pkl \
     -p "appImageName=ghcr.io/nozzlegear/foxy-balance:latest" \
     --multiple-file-output ~/.config/containers/systemd/
   ```

2. Reload systemd:
   ```bash
   systemctl --user daemon-reload
   ```

3. Start the service:
   ```bash
   systemctl --user start foxy-balance-app.service
   ```

## Usage

- Start: `systemctl --user start foxy-balance-app.service`
- Stop: `systemctl --user stop foxy-balance-app.service`
- Status: `systemctl --user status foxy-balance-app.service`
- App logs: `journalctl --user -u foxy-balance-app.service -f`
- DB logs: `journalctl --user -u foxy-balance-db.service -f`
- Restart: `systemctl --user restart foxy-balance-app.service`

## Notes

- The app container depends on `foxy-balance-db.service` and will not start until the DB is ready
- The DB volume (`foxybalance-pvc`) persists data between restarts and redeployments
- The release workflow pins the app image to a specific digest on each deploy

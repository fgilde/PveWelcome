# PveWelcome

*🇩🇪 Deutsch: [README_de.md](README_de.md)*

A small, branded landing page **and** an internal dashboard for your Proxmox node — in one app.
Publicly it shows your brand; once logged in you get node health, every VM/container with
start/stop/restart, storage, backups (backup **and** restore at the click of a button), and your
Nginx Proxy Manager domains with a real external-reachability check.

Runs as a single .NET Blazor Server process. The server keeps the secrets and calls the
Proxmox/NPM APIs server-side — the browser never sees a token.

<img width="1530" height="1028" alt="PveWelcome Dashboard" src="https://github.com/user-attachments/assets/8c2c51ff-e791-4d74-b568-f770ad8fe341" />

## Features

- **Node health** — CPU, RAM, load, uptime, storage pools (used/total), history sparklines, pending updates.
- **Guests as tiles** — live status, start/stop/restart (hard confirmation), CPU/RAM/disk, one-click noVNC console.
- **Backups** — latest backup per guest, **Backup now** and **Restore** (destructive, name confirmation).
- **Snapshots** — create / roll back / delete per guest.
- **Scheduled backup jobs** — the cluster's backup jobs listed, with an enable/disable toggle.
- **Activity feed + task logs** — recent PVE tasks (backup/restore/start/stop) with live status; click one to read its log.
- **Disk SMART health** — per-disk health with an alert when a disk isn't `PASSED`.
- **Domains + uptime monitors** — which domain serves which resource plus a real external check, and your own
  arbitrary HTTP monitors.
- **Alerts + notifications** — pool >90 %, stopped guests, guests without a backup, pending updates, dying disks,
  monitors down; optionally pushed via webhook/Telegram.
- **Security** — cookie auth, optional per-user **TOTP 2FA**, and login rate-limiting/lockout.
- **External watchdog** — a systemd timer on the Proxmox host pings PveWelcome and alerts via Telegram if it dies
  (see `scripts/pve-install.sh` output / docs).
- **Branding** — a per-domain landing page (name, tagline, accent color, link) from the DB.
- **Everything configurable in the UI** — PVE/NPM access, backup target, notifications, monitors, users, 2FA, branding under `/admin`.
- **Configurable AI integration** - Ask or change stuff directly in your Pve Environment

<img width="1954" height="884" alt="image" src="https://github.com/user-attachments/assets/97926ba2-7a3a-4c8b-af7e-6da191f37a6e" />


<img width="1943" height="1221" alt="image" src="https://github.com/user-attachments/assets/935b6ae6-76e2-4290-8a8b-88d4ef7b1ccb" />


## Requirements

- A Proxmox VE (8.x) with an **API token** (`Datacenter → Permissions → API Tokens`):
  `pveum user token add root@pam pvewelcome --privsep 0` (restore/backup need write access).
- Optional: an Nginx Proxy Manager (for the domain overview).
- Network access from wherever PveWelcome runs to Proxmox (`:8006`) and, if used, NPM.

## Install

### A) Proxmox helper (recommended — one-shot)

Run on the **Proxmox host**. Creates a small Debian LXC and runs PveWelcome natively as a
systemd service (no Docker needed):

```bash
bash -c "$(curl -fsSL https://raw.githubusercontent.com/fgilde/PveWelcome/master/scripts/pve-install.sh)"
```

Prompts for the PVE URL, token and admin login, then serves on `http://<ct-ip>:8080`.
Resources/storage/TZ are overridable via env, e.g. `RAM=768 STORAGE=local-lvm bash pve-install.sh`.

### B) Docker (any Docker host: VM, NAS, VPS)

```bash
bash -c "$(curl -fsSL https://raw.githubusercontent.com/fgilde/PveWelcome/master/install.sh)"
```

Or directly:

```bash
docker run -d --name pvewelcome --restart unless-stopped \
  -p 8091:8080 -v pvewelcome-data:/data --user 0:0 \
  -e Pve__BaseUrl="https://192.168.1.10:8006/api2/json" \
  -e Pve__ApiToken="root@pam!pvewelcome=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" \
  -e Admin__User="admin" -e Admin__Password="changeme" \
  -e Db__Path=/data/pvewelcome.db -e ASPNETCORE_ENVIRONMENT=Production \
  ghcr.io/fgilde/pvewelcome:latest
```

The `/data` volume holds the DB **and** the data-protection keys — don't throw it away, or you'll
get a login 500 after every restart. `--user 0:0` so the container can write to the volume.

### C) From source

```bash
dotnet run                # local, seeds admin/admin
# or build your own image:
dotnet publish -c Release --os linux --arch x64 -t:PublishContainer -p:ContainerImageTag=dev
```

## Configuration

<img width="1938" height="1239" alt="image" src="https://github.com/user-attachments/assets/7946d2ae-0d3e-43fd-a2e1-e7faba6cfa02" />


Everything via env (or `appsettings.json`); double `__` = nested section. After the first start
most of it is also editable in the UI under **`/admin/settings → Connections`** (DB-backed) —
handy when others point the app at their own Proxmox/NPM.

| Env | Purpose |
|-----|---------|
| `Pve__BaseUrl` | e.g. `https://IP:8006/api2/json` |
| `Pve__ApiToken` | full token `USER@REALM!TOKENID=SECRET` |
| `Npm__BaseUrl` / `Npm__User` / `Npm__Password` | Nginx Proxy Manager (optional) |
| `Admin__User` / `Admin__Password` | first login (change later in `/admin/users`) |
| `Db__Path` | SQLite path, e.g. `/data/pvewelcome.db` |
| `Brands__<host>__Name` etc. | per-domain branding (see `appsettings.json`) |

Escape a `$` in passwords as `$$` in a Compose file (Compose interpolation).

## Where does this make sense?

- **On Proxmox itself** (option A) — the obvious homelab variant: one CT, done.
- **On a separate Docker host / NAS / VPS** — if you don't want to load the node; only needs
  network/VPN visibility to the Proxmox API.
- **As a public landing page** behind a Cloudflare Tunnel + reverse proxy — the brand on the
  outside, the login area stays internal.

## How it works

.NET 10 Blazor Server · EF Core SQLite · cookie auth · a background service caches PVE+NPM data
every 20 s (navigation without waiting). `PveClient` (Proxmox REST) + `NpmClient` (NPM API).
Proxmox uses a self-signed cert internally — the PVE HTTP client skips cert validation only for that.
TLS terminates at the reverse proxy/tunnel; the app doesn't force HTTPS itself.

## License

MIT.

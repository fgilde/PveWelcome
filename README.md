# PveWelcome

Branded landing page + internal console for a Proxmox VE node.

- **Public landing** — per-domain branded (host header), points visitors to an external projects URL, offers login.
- **Authenticated dashboard** — node health, all VMs/CTs with live status, start/stop/restart, and the Nginx-Proxy-Manager bindings.
- **Resource detail** — correlates a guest's IP with the NPM domains it serves, plus a Proxmox link.
- **Users** — admin seeded from env; admins can create/delete users. Roles are prepared for finer permissions later.



<img width="1530" height="1028" alt="image" src="https://github.com/user-attachments/assets/8c2c51ff-e791-4d74-b568-f770ad8fe341" />


## Stack

.NET 10 Blazor Server · SQLite (users) · cookie auth · custom CSS. No external UI framework.
The server holds the Proxmox / NPM secrets and calls their REST APIs; nothing sensitive reaches the browser.

## Configuration (environment variables)

| Var | Example |
| --- | --- |
| `Pve__BaseUrl` | `https://192.168.1.10:8006/api2/json` |
| `Pve__ApiToken` | `root@pam!mytoken=<secret>` |
| `Npm__BaseUrl` | `http://192.168.1.20:81` |
| `Npm__User` / `Npm__Password` | NPM admin login |
| `Admin__User` / `Admin__Password` | seeded on first run if no users exist |
| `Db__Path` | `/data/pvewelcome.db` (mount a volume) |
| `Brands__<host>__Name` etc. | per-domain branding (see `appsettings.json`) |

Create a Proxmox API token with write access (start/stop):
`pveum user token add root@pam mytoken --privsep 0`

## Run

```bash
dotnet run                       # dev (seeds admin/admin, local sqlite)
```

## Container

```bash
dotnet publish -c Release --os linux --arch x64 -t:PublishContainer \
  -p:ContainerRepository=pvewelcome -p:ContainerImageTag=v1
```

Runs on port 8080. Mount a volume at `/data` for the SQLite database.

## Notes

- Proxmox uses a self-signed cert on the internal network; the PVE HTTP client skips cert validation for that host only.
- Behind a reverse proxy / tunnel that terminates TLS — the app does not force HTTPS itself.

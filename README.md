# PveWelcome

Eine kleine, gebrandete Landing-Page **und** ein internes Dashboard für deinen Proxmox-Node —
in einer App. Öffentlich zeigt sie deine Marke; eingeloggt siehst du Node-Health, alle
VMs/Container mit Start/Stop/Restart, Storage, Backups (inkl. Backup **und** Restore auf Knopfdruck)
und deine Nginx-Proxy-Manager-Domains mit echtem Extern-Status.

Läuft als ein einzelner .NET-Blazor-Server-Prozess. Der Server hält die Secrets und ruft die
Proxmox-/NPM-APIs serverseitig — der Browser sieht keine Tokens.

<img width="1530" height="1028" alt="PveWelcome Dashboard" src="https://github.com/user-attachments/assets/8c2c51ff-e791-4d74-b568-f770ad8fe341" />

## Features

- **Node-Health** — CPU, RAM, Load, Uptime, Storage-Pools (used/total).
- **Guests als Kacheln** — Live-Status, Start/Stop/Restart (harte Sicherheitsabfrage), CPU/RAM/Disk.
- **Backups** — letztes Backup je Guest, **Backup jetzt** und **Restore** (destruktiv, mit Namens-Bestätigung).
- **NPM-Domains** — welche Domain welche Ressource bedient, plus echter externer Erreichbarkeits-Check.
- **Alerts** — Pool >90 %, gestoppte Guests, Guests ohne Backup.
- **Branding** — pro Domain eigene Landing-Page (Name, Tagline, Akzentfarbe, Link) aus der DB.
- **Alles konfigurierbar in der UI** — PVE-/NPM-Zugang, Backup-Ziel, User, Branding unter `/admin`.

## Voraussetzungen

- Ein Proxmox VE (8.x) mit einem **API-Token** (`Datacenter → Permissions → API Tokens`):
  `pveum user token add root@pam pvewelcome --privsep 0` (Restore/Backup brauchen Schreibrechte).
- Optional: ein Nginx Proxy Manager (für die Domain-Übersicht).
- Netzwerkzugang von dort, wo PveWelcome läuft, zu Proxmox (`:8006`) und ggf. NPM.

## Installieren

### A) Proxmox-Helper (empfohlen — „einfach ballern")

Auf dem **Proxmox-Host** ausführen. Legt einen kleinen Debian-LXC an und startet PveWelcome
nativ als systemd-Service (kein Docker nötig):

```bash
bash -c "$(curl -fsSL https://raw.githubusercontent.com/fgilde/PveWelcome/master/scripts/pve-install.sh)"
```

Fragt PVE-URL, Token und Admin-Login ab, danach läuft es auf `http://<ct-ip>:8080`.
Ressourcen/Storage/TZ via Env-Overrides steuerbar, z. B. `RAM=768 STORAGE=local-lvm bash pve-install.sh`.

### B) Docker (jeder Docker-Host: VM, NAS, VPS)

```bash
bash -c "$(curl -fsSL https://raw.githubusercontent.com/fgilde/PveWelcome/master/install.sh)"
```

Oder direkt:

```bash
docker run -d --name pvewelcome --restart unless-stopped \
  -p 8091:8080 -v pvewelcome-data:/data --user 0:0 \
  -e Pve__BaseUrl="https://192.168.1.10:8006/api2/json" \
  -e Pve__ApiToken="root@pam!pvewelcome=xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx" \
  -e Admin__User="admin" -e Admin__Password="changeme" \
  -e Db__Path=/data/pvewelcome.db -e ASPNETCORE_ENVIRONMENT=Production \
  ghcr.io/fgilde/pvewelcome:latest
```

Das `/data`-Volume hält DB **und** Data-Protection-Keys — nicht wegwerfen, sonst
Login-500 nach jedem Neustart. `--user 0:0`, damit der Container ins Volume schreiben darf.

### C) Aus dem Quellcode

```bash
dotnet run                # lokal, seedet admin/admin
# oder ein eigenes Image bauen:
dotnet publish -c Release --os linux --arch x64 -t:PublishContainer -p:ContainerImageTag=dev
```

## Konfiguration

Alles per Env (oder `appsettings.json`); Doppel-`__` = verschachtelte Section. Nach dem ersten
Start ist das meiste auch in der UI unter **`/admin/settings → Verbindungen`** editierbar
(DB-gestützt) — praktisch, wenn andere die App auf ihre eigene Proxmox/NPM zeigen wollen.

| Env | Zweck |
|-----|-------|
| `Pve__BaseUrl` | z. B. `https://IP:8006/api2/json` |
| `Pve__ApiToken` | voller Token `USER@REALM!TOKENID=SECRET` |
| `Npm__BaseUrl` / `Npm__User` / `Npm__Password` | Nginx Proxy Manager (optional) |
| `Admin__User` / `Admin__Password` | erster Login (danach in `/admin/users` änderbar) |
| `Db__Path` | SQLite-Pfad, z. B. `/data/pvewelcome.db` |
| `Brands__<host>__Name` etc. | Branding pro Domain (siehe `appsettings.json`) |

Passwörter mit `$` in einer Compose-Datei als `$$` schreiben (Compose-Interpolation).

## Wo läuft das sinnvoll?

- **Auf Proxmox selbst** (Weg A) — die naheliegende Homelab-Variante, ein CT, fertig.
- **Auf einem separaten Docker-Host / NAS / VPS** — wenn du den Node nicht belasten willst;
  braucht nur Netzwerk-/VPN-Sicht auf die Proxmox-API.
- **Als öffentliche Landing** hinter Cloudflare-Tunnel + Reverse Proxy — außen die Marke,
  der Login-Bereich bleibt intern.

## Technik

.NET 10 Blazor Server · EF Core SQLite · Cookie-Auth · ein Hintergrund-Dienst cached PVE+NPM-Daten
alle 20 s (Navigation ohne Warten). `PveClient` (Proxmox REST) + `NpmClient` (NPM-API).
Proxmox nutzt intern ein self-signed Cert — der PVE-HTTP-Client überspringt die Cert-Prüfung nur dafür.
TLS terminiert am Reverse Proxy/Tunnel; die App erzwingt selbst kein HTTPS.

## Lizenz

MIT.

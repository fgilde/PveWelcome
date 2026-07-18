# PveWelcome — Design

**Datum:** 2026-07-18
**Repo:** `C:\dev\privat\github\PveWelcome`
**Deploy:** Container über Coolify (dogfoodet die Aspire→Registry→Coolify-Strecke)

## Ziel

Eine gebrandete Landing-Page für den Proxmox-Node mit zwei Gesichtern:
- **Öffentlich (nicht eingeloggt):** cooler, gebrandeter Auftritt; verweist auf `gilde.org` für Projekte/Infos; bietet Login.
- **Eingeloggt:** Dashboard über die PVE-Ressourcen — Status/Health sehen, steuern (start/stop), pro Ressource eine Detailseite mit allen sinnvollen Links; Übersicht der NPM-Bindings.

Multi-Brand: dieselbe App zeigt je nach Domain einen anderen Namen (nksoft.de → „nksoft", gilde.org → „gilde", …).

## Tech-Entscheidung

**Blazor Server** (nicht WASM). Grund: der Server hält die PVE-/NPM-Secrets und ruft die APIs serverseitig — keine Tokens im Browser. Live-Health-Updates über SignalR (in Blazor Server eingebaut). Standard-Stack des Nutzers, deploybar über die vorhandene Coolify-Pipeline.

- .NET 10, Blazor Server.
- **SQLite** für den User-Store (leicht, ein Volume, passt in einen Container).
- Passwort-Hashing (ASP.NET Core `PasswordHasher` oder Identity — s. offene Entscheidung).
- Styling: modernes CSS/Tailwind, Fokus auf die öffentliche Landing.

## Komponenten

### 1. Öffentliche Landing (`/`)
- Gebrandeter Hero, dezente Animation, „cool". Marken-Name aus Host-Header.
- Verweis „Projekte & Infos → gilde.org" (externer Link).
- Login-Button → `/login`.

### 2. Auth
- **User-Store (SQLite):** Tabelle `Users` (Id, Username, PasswordHash, Role, CreatedAt).
- **Seed:** beim ersten Start Admin aus Env (`ADMIN_USER`, `ADMIN_PASSWORD`) anlegen, falls kein User existiert.
- **Cookie-basierte Anmeldung** (ASP.NET Core Authentication).
- **User-Verwaltung:** Admin kann weitere User anlegen/löschen (UI unter `/admin/users`). v1 kann minimal sein, Struktur ist da.
- **Rollen vorbereitet:** Feld `Role` (v1: `Admin` darf alles). Feinere Rechte später.

### 3. PVE-Integration (`PveClient`)
- Spricht das **Proxmox REST-API** (`https://192.168.178.126:8006/api2/json`) mit **API-Token** (serverseitig, Schreibrechte für start/stop).
- Liest: Node-Health (CPU/RAM/Uptime/Load), Liste VMs (`qm`)/CTs (`pct`) mit Name/ID/Typ/Status/Ressourcen.
- Aktionen: start/stop/restart pro Guest.

### 4. NPM-Integration (`NpmClient`)
- Spricht das **Nginx-Proxy-Manager-API** (`http://192.168.178.100:81/api`) mit Login-Token.
- Liest: alle Proxy-Hosts (Domain(s) → forward_host:port, enabled, online).

### 5. Dashboard (`/dashboard`)
- **Ressourcen-Übersicht:** Karten/Tabelle aller VMs/CTs mit Live-Status + Health der Node. Start/Stop/Restart-Buttons.
- **NPM-Bindings-Übersicht:** alle Domains → Ziel, Klick öffnet die Domain.

### 6. Ressource-Detailseite (`/resource/{id}`)
- Alle Infos zur VM/CT (Ressourcen, Status, IP, Config-Auszug).
- **Alle sinnvollen Links:**
  - Proxmox-Konsole der Guest (`https://192.168.178.126:8006/...` mit Kontext).
  - **Korrelierte NPM-Domains:** die Proxy-Hosts, deren `forward_host` = IP dieser Guest → „diese VM bedient cooltest.nksoft.de, coolify.nksoft.de".
  - Start/Stop/Restart.

### 7. Multi-Brand (`BrandResolver`)
- Config-Map `Host → { Name, evtl. Farben/Logo }` (nksoft.de → „nksoft", gilde.org → „gilde"). App liest den Host-Header, wählt Branding. Fallback-Default. Neue Marke = ein Config-Eintrag (appsettings/Env).

## Datenfluss

```
Browser (Domain X)
  → PveWelcome (Blazor Server, Coolify)
      BrandResolver: Host → Name
      [nicht eingeloggt] → Landing (gilde.org-Link, Login)
      [eingeloggt]
        → PveClient  → Proxmox REST-API (192.168.178.126:8006, Token)
        → NpmClient  → NPM-API (192.168.178.100:81)
        → Korrelation PVE-IP ↔ NPM forward_host
```

## Deploy

- `dotnet publish -t:PublishContainer` → Registry (PVE) → Coolify-Resource.
- **Volume** für die SQLite-Datei (persistente User).
- **Env:** `PVE_API_URL`, `PVE_API_TOKEN`, `NPM_URL`, `NPM_USER`, `NPM_PASSWORD`, `ADMIN_USER`, `ADMIN_PASSWORD`, Brand-Config.
- Domain via NPM/Coolify → `www.nksoft.de` (und weitere Marken-Domains auf dieselbe App).

## Sicherheit

- App hält **PVE-Token mit Schreibrechten** (start/stop) — mächtig. Nur hinter Login erreichbar; über unsere Domains. Bewusst akzeptiert.
- PVE-API-TLS ist self-signed → Client-seitig Zertifikatsprüfung für diesen Host deaktivieren (oder Cert pinnen). Nur intern.
- Passwörter gehasht, nie im Klartext. Admin-Seed nur beim ersten Start.

## Offene Entscheidungen (im Plan zu fixieren)

1. **Auth-Framework:** ASP.NET Core Identity (bringt User-CRUD/Rollen/Hashing fertig, aber viel Scaffolding) **vs.** schlanke Custom-Auth (eigene Users-Tabelle + `PasswordHasher` + Cookie). Empfehlung: **schlanke Custom-Auth** für <10 User (ponytail), Identity nur falls Feinrechte/Externe später komplex werden.
2. **Styling:** Tailwind (via CDN/Build) vs. handgeschriebenes CSS. Entscheidung bei der Landing-Umsetzung.

## Bewusst weggelassen (YAGNI, v1)

- OAuth/SSO — nur lokaler Login.
- Feingranulare Rollen/Rechte — nur Admin, Struktur vorbereitet.
- Aktionen über start/stop hinaus (Snapshots, Migration, Backups) — später.
- Metriken-Historie/Graphen — v1 zeigt Live-Werte, keine Zeitreihen.

#!/usr/bin/env bash
# PveWelcome — Proxmox helper. Run ON the Proxmox host. Creates a small Debian LXC and runs
# PveWelcome natively as a systemd service (no Docker). Then open it and configure the rest in the UI.
#
#   bash -c "$(curl -fsSL https://raw.githubusercontent.com/fgilde/PveWelcome/master/scripts/pve-install.sh)"
#
# Override any default via env, e.g.  CTID=250 RAM=768 STORAGE=local-lvm bash pve-install.sh
set -euo pipefail

CTID="${CTID:-$(pvesh get /cluster/nextid)}"
HOSTNAME_="${HOSTNAME_:-pvewelcome}"
DISK="${DISK:-4}"                    # GiB
RAM="${RAM:-512}"                    # MiB
CORES="${CORES:-1}"
BRIDGE="${BRIDGE:-vmbr0}"
STORAGE="${STORAGE:-local-lvm}"      # where the CT rootfs lives
TEMPLATE_STORE="${TEMPLATE_STORE:-local}"
TZ_="${TZ_:-Europe/Berlin}"
REL="https://github.com/fgilde/PveWelcome/releases/latest/download/pvewelcome-linux-x64.tar.gz"

command -v pct >/dev/null || { echo "pct nicht gefunden — auf dem Proxmox-Host ausführen."; exit 1; }

echo "== PveWelcome LXC $CTID ($HOSTNAME_) =="

# 1) Debian 12 template
TMPL_FILE=$(pveam available --section system | awk '/debian-12-standard/{print $2}' | sort | tail -1)
pveam list "$TEMPLATE_STORE" | grep -q "$TMPL_FILE" || pveam download "$TEMPLATE_STORE" "$TMPL_FILE"
TMPL="$TEMPLATE_STORE:vztmpl/$TMPL_FILE"

# 2) create + start the container (unprivileged, DHCP)
pct create "$CTID" "$TMPL" -hostname "$HOSTNAME_" -cores "$CORES" -memory "$RAM" \
  -net0 "name=eth0,bridge=$BRIDGE,ip=dhcp" -rootfs "$STORAGE:$DISK" -unprivileged 1 -onboot 1 >/dev/null
pct start "$CTID"
for i in $(seq 1 20); do pct exec "$CTID" -- test -e /dev/null 2>/dev/null && break; sleep 1; done

run(){ pct exec "$CTID" -- bash -c "$1"; }

# 3) deps + binary (self-contained; invariant globalization -> no ICU needed)
run "apt-get update -qq && apt-get install -y -qq curl ca-certificates >/dev/null"
run "mkdir -p /opt/pvewelcome /data && curl -fsSL '$REL' | tar -xz -C /opt/pvewelcome && chmod +x /opt/pvewelcome/PveWelcome"

# 4) config (only the essentials — the rest is editable in /admin/settings later)
echo
read -rp  "Proxmox API URL (https://IP:8006/api2/json): " PVE_URL
read -rp  "Proxmox API Token (USER@REALM!ID=SECRET): "     PVE_TOKEN
read -rp  "Admin Login [admin]: " ADMIN_USER; ADMIN_USER=${ADMIN_USER:-admin}
read -rsp "Admin Passwort: " ADMIN_PASS; echo

run "cat >/etc/pvewelcome.env <<EOF
Pve__BaseUrl=$PVE_URL
Pve__ApiToken=$PVE_TOKEN
Admin__User=$ADMIN_USER
Admin__Password=$ADMIN_PASS
Db__Path=/data/pvewelcome.db
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:8080
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
TZ=$TZ_
EOF
chmod 600 /etc/pvewelcome.env"

run "cat >/etc/systemd/system/pvewelcome.service <<EOF
[Unit]
Description=PveWelcome
After=network-online.target
Wants=network-online.target
[Service]
EnvironmentFile=/etc/pvewelcome.env
WorkingDirectory=/opt/pvewelcome
ExecStart=/opt/pvewelcome/PveWelcome
Restart=always
[Install]
WantedBy=multi-user.target
EOF
systemctl enable --now pvewelcome"

IP=$(pct exec "$CTID" -- hostname -I | awk '{print $1}')
echo
echo "Fertig. PveWelcome:  http://$IP:8080   (Login: $ADMIN_USER)"
echo "NPM/Backup-Ziel etc. unter  /admin/settings -> Verbindungen."

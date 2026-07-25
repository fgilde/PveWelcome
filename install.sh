#!/usr/bin/env bash
# PveWelcome — Docker installer. Runs the container on any Docker host that can reach your Proxmox.
#   bash -c "$(curl -fsSL https://raw.githubusercontent.com/fgilde/PveWelcome/master/install.sh)"
set -euo pipefail

IMAGE="${PVEWELCOME_IMAGE:-ghcr.io/fgilde/pvewelcome:latest}"
PORT="${PORT:-8091}"

ask()  { local p="$1" d="${2:-}" v; read -rp "$p${d:+ [$d]}: " v; echo "${v:-$d}"; }
asks() { local p="$1" v; read -rsp "$p: " v; echo >&2; echo "$v"; }   # silent (passwords)

command -v docker >/dev/null || { echo "docker fehlt — erst Docker installieren."; exit 1; }

echo "== PveWelcome install =="
PVE_URL=$(ask   "Proxmox API URL (https://IP:8006/api2/json)")
PVE_TOKEN=$(ask "Proxmox API Token (USER@REALM!ID=SECRET)")
NPM_URL=$(ask   "NPM URL (leer = ohne NPM)" "")
NPM_USER=$(ask  "NPM User" "")
NPM_PASS=$([ -n "$NPM_URL" ] && asks "NPM Passwort" || echo "")
ADMIN_USER=$(ask "Admin Login" "admin")
ADMIN_PASS=$(asks "Admin Passwort")

docker pull "$IMAGE"
docker rm -f pvewelcome 2>/dev/null || true
docker run -d --name pvewelcome --restart unless-stopped \
  -p "$PORT:8080" -v pvewelcome-data:/data --user 0:0 \
  -e Pve__BaseUrl="$PVE_URL" -e Pve__ApiToken="$PVE_TOKEN" \
  -e Npm__BaseUrl="$NPM_URL" -e Npm__User="$NPM_USER" -e Npm__Password="$NPM_PASS" \
  -e Admin__User="$ADMIN_USER" -e Admin__Password="$ADMIN_PASS" \
  -e Db__Path=/data/pvewelcome.db -e ASPNETCORE_ENVIRONMENT=Production \
  "$IMAGE"

echo
echo "PveWelcome läuft auf  http://localhost:$PORT   (Login: $ADMIN_USER)"
echo "Später alles unter /admin/settings -> Verbindungen änderbar."

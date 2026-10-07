#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 Marcello Anderlini
# Preparazione del server (una volta): Docker dai repository ufficiali, firewall.
# Uso dal PC di sviluppo:  ssh <host-ssh> 'sudo bash -s' < deploy/production/setup-server.sh
set -euo pipefail

. /etc/os-release
echo "==> $PRETTY_NAME"

if ! command -v docker >/dev/null; then
  echo "==> Installazione di Docker (download.docker.com, $VERSION_CODENAME)"
  apt-get update -q
  apt-get install -y -q ca-certificates curl
  install -m 0755 -d /etc/apt/keyrings
  curl -fsSL https://download.docker.com/linux/debian/gpg -o /etc/apt/keyrings/docker.asc
  chmod a+r /etc/apt/keyrings/docker.asc
  echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/debian $VERSION_CODENAME stable" \
    > /etc/apt/sources.list.d/docker.list
  apt-get update -q
  apt-get install -y -q docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
fi
systemctl enable --now docker
docker --version
docker compose version

echo "==> Firewall: SSH, HTTP, HTTPS"
apt-get install -y -q ufw
ufw default deny incoming >/dev/null
ufw default allow outgoing >/dev/null
ufw allow 22/tcp >/dev/null
ufw allow 80/tcp >/dev/null
ufw allow 443/tcp >/dev/null
ufw --force enable >/dev/null
ufw status | head -8

install -d -m 0750 /opt/vedettavip
echo "==> Server pronto"

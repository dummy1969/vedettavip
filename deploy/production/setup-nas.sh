#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 Marcello Anderlini
# Copia esterna dei backup su una share SMB/CIFS (NAS). Da lanciare sul server, in un terminale (chiede la password):
#
#   sudo /opt/vedettavip/src/deploy/production/setup-nas.sh //nas.example.lan/backup backup-user [sottocartella]
#
# - installa cifs-utils;
# - salva le credenziali in /root/.vedettavip-nas.cred (600, root): la password non passa da riga di comando né dalla history;
# - monta la share su /mnt/vedettavip-nas con systemd automount (montata solo quando serve, nofail: un NAS spento non
#   blocca l'avvio della VM), file accessibili solo a root;
# - imposta BACKUP_COPY_DIR in /opt/vedettavip/vedettavip.env (default sottocartella "vedettavip") e lancia un backup di prova.
# Rilanciabile: sostituisce credenziali e voce di fstab precedenti. Facoltativi: SMB_DOMAIN, SMB_VERS (es. 3.0).
set -euo pipefail
trap 'echo "ERRORE alla riga $LINENO: $BASH_COMMAND" >&2' ERR

SHARE=${1:-}; SMB_USER=${2:-}; SUBDIR=${3:-vedettavip}
MNT=/mnt/vedettavip-nas
CRED=/root/.vedettavip-nas.cred
ENV_FILE=/opt/vedettavip/vedettavip.env
[ "$(id -u)" -eq 0 ] || { echo "Va eseguito con sudo."; exit 1; }
[[ "$SHARE" == //*/* && -n "$SMB_USER" ]] || { sed -n '4,13p' "$0"; exit 2; }

echo "==> cifs-utils"
dpkg -s cifs-utils >/dev/null 2>&1 || { apt-get update -qq && DEBIAN_FRONTEND=noninteractive apt-get install -y -qq cifs-utils; }

echo "==> credenziali per $SMB_USER su $SHARE"
read -rsp "Password di $SMB_USER: " SMB_PASS; echo
[ -n "$SMB_PASS" ] || { echo "Password vuota."; exit 1; }
( umask 077
  { echo "username=$SMB_USER"; echo "password=$SMB_PASS"
    if [ -n "${SMB_DOMAIN:-}" ]; then echo "domain=$SMB_DOMAIN"; fi; } > "$CRED" )
unset SMB_PASS
chmod 600 "$CRED"

echo "==> montaggio su $MNT (fstab, systemd automount)"
mkdir -p "$MNT"
OPTS="credentials=$CRED,uid=0,gid=0,file_mode=0600,dir_mode=0700${SMB_VERS:+,vers=$SMB_VERS},_netdev,nofail,x-systemd.automount,x-systemd.idle-timeout=10min,x-systemd.mount-timeout=30"
cp /etc/fstab /etc/fstab.bak-vedettavip-nas
grep -v "[[:space:]]$MNT[[:space:]]" /etc/fstab.bak-vedettavip-nas > /etc/fstab
echo "$SHARE $MNT cifs $OPTS 0 0" >> /etc/fstab
UNIT=$(systemd-escape --path "$MNT")
systemctl daemon-reload
systemctl stop "$UNIT.mount" 2>/dev/null || true
systemctl restart "$UNIT.automount"

echo "==> prova di scrittura"
if ! mkdir -p "$MNT/$SUBDIR" || ! touch "$MNT/$SUBDIR/.vedettavip-write-test" || ! rm "$MNT/$SUBDIR/.vedettavip-write-test"; then
  echo "Montaggio o scrittura non riusciti. Ultimi messaggi del kernel:"
  dmesg | grep -i cifs | tail -5
  echo "Tipico: 'error -13' = utente/password o permessi della share; 'error -95'/'-112' = provare SMB_VERS=3.0 o 2.1."
  exit 1
fi
df -h "$MNT" | tail -1

echo "==> BACKUP_COPY_DIR=$MNT/$SUBDIR in $ENV_FILE"
if grep -q '^BACKUP_COPY_DIR=' "$ENV_FILE"; then
  sed -i "s|^BACKUP_COPY_DIR=.*|BACKUP_COPY_DIR=$MNT/$SUBDIR|" "$ENV_FILE"
else
  printf '\n# Copia esterna dei backup (setup-nas.sh)\nBACKUP_COPY_DIR=%s\n' "$MNT/$SUBDIR" >> "$ENV_FILE"
fi

echo "==> backup di prova"
systemctl start vedettavip-backup.service && journalctl -u vedettavip-backup.service -n 6 --no-pager -o cat
ls -l "$MNT/$SUBDIR"
echo "Con deploy.sh --config il .env del server viene sostituito a ogni deploy: aggiungere"
echo "  BACKUP_COPY_DIR=$MNT/$SUBDIR"
echo "anche nel .env della cartella di configurazione."

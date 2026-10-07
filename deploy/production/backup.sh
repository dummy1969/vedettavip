#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 Marcello Anderlini
# Backup di VedettaVip: dump del database (formato custom, verificato), segreti (vedettavip.env) e CA di Caddy.
# Lanciato ogni notte da vedettavip-backup.timer (systemd), come root; a mano:  sudo /opt/vedettavip/src/deploy/production/backup.sh
#
# Ogni esecuzione crea  $BACKUP_DIR/vedettavip-AAAAMMGG-HHMMSS/  con:
#   vedettavip.dump     pg_dump -Fc (configurazione, eventi, metriche, utenti, chiavi di Data Protection)
#   vedettavip.env      password del DB, chiave degli agenti
#   caddy-data.tar.gz   CA interna e certificati (senza, i browser vanno riconfigurati con una nuova CA)
#   SHA256SUMS
# Il backup contiene segreti: cartella 700 di root, e così va trattata anche la copia esterna.
#
# Impostazioni (in vedettavip.env, tutte facoltative):
#   BACKUP_DIR=/var/backups/vedettavip   BACKUP_KEEP_DAYS=14
#   BACKUP_COPY_DIR=/mnt/nas/vedettavip  copia di ogni backup su un disco esterno/NAS già montato (stessa conservazione)
set -euo pipefail

VEDETTAVIP_DIR=${VEDETTAVIP_DIR:-/opt/vedettavip}
ENV_FILE=${ENV_FILE:-$VEDETTAVIP_DIR/vedettavip.env}
DB_CONTAINER=${DB_CONTAINER:-vedettavip-db-1}
CADDY_CONTAINER=${CADDY_CONTAINER-vedettavip-caddy-1}   # vuoto = salta la CA

set -a; . "$ENV_FILE"; set +a
BACKUP_DIR=${BACKUP_DIR:-/var/backups/vedettavip}
BACKUP_KEEP_DAYS=${BACKUP_KEEP_DAYS:-14}
BACKUP_COPY_DIR=${BACKUP_COPY_DIR:-}
# "netmap": nome storico di utente e database, mantenuto dopo la rinomina in VedettaVip
DB_USER=${POSTGRES_USER:-netmap}
DB_NAME=${POSTGRES_DB:-netmap}

log() { echo "$(date '+%F %T') $*"; }

umask 077
mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"
NAME=vedettavip-$(date +%Y%m%d-%H%M%S)
WORK="$BACKUP_DIR/.$NAME.partial"
trap 'rm -rf "$WORK"' EXIT
mkdir "$WORK"

log "dump del database $DB_NAME"
if ! docker exec "$DB_CONTAINER" pg_dump -U "$DB_USER" -d "$DB_NAME" -Fc > "$WORK/vedettavip.dump" 2> "$WORK/pg_dump.err"; then
  cat "$WORK/pg_dump.err" >&2; exit 1
fi
# Avviso noto e innocuo di TimescaleDB (FK circolari nel catalogo dei continuous aggregate): non lo si ripete ogni notte
grep -vE 'circular foreign-key|continuous_agg|disable-triggers|data-only dump' "$WORK/pg_dump.err" >&2 || true
rm "$WORK/pg_dump.err"
# Verifica: il dump deve essere leggibile per intero e contenere le tabelle principali
TOC=$(docker exec -i "$DB_CONTAINER" pg_restore --list < "$WORK/vedettavip.dump")
for table in Devices Maps Events AspNetUsers DataProtectionKeys; do
  grep -q "TABLE DATA public \"\?$table\"\? " <<<"$TOC" || { log "ERRORE: tabella $table assente nel dump"; exit 1; }
done

cp "$ENV_FILE" "$WORK/vedettavip.env"
if [ -n "$CADDY_CONTAINER" ]; then
  log "CA e certificati di Caddy"
  docker exec "$CADDY_CONTAINER" tar -czf - -C /data . > "$WORK/caddy-data.tar.gz"
fi
(cd "$WORK" && sha256sum -- * > SHA256SUMS)
mv "$WORK" "$BACKUP_DIR/$NAME"
trap - EXIT
log "backup creato: $BACKUP_DIR/$NAME ($(du -sh "$BACKUP_DIR/$NAME" | cut -f1))"

# Conservazione: elimina i backup più vecchi di BACKUP_KEEP_DAYS giorni (mai l'ultimo appena creato).
# netmap-*: backup creati prima della rinomina in VedettaVip, scadono con la stessa regola.
prune() {
  find "$1" -mindepth 1 -maxdepth 1 -type d \( -name 'vedettavip-*' -o -name 'netmap-*' \) -mtime +"$BACKUP_KEEP_DAYS" -print -exec rm -rf {} + |
    sed "s|^|$(date '+%F %T') eliminato |"
}
prune "$BACKUP_DIR"

if [ -n "$BACKUP_COPY_DIR" ]; then
  # Mai scrivere sul disco locale se il NAS non è montato: la copia "esterna" finirebbe nella stessa VM
  if ! mountpoint -q "$BACKUP_COPY_DIR" && ! mountpoint -q "$(dirname "$BACKUP_COPY_DIR")"; then
    log "ERRORE: $BACKUP_COPY_DIR non è su un filesystem montato, copia esterna non eseguita"
    exit 1
  fi
  mkdir -p "$BACKUP_COPY_DIR"
  log "copia su $BACKUP_COPY_DIR"
  cp -r "$BACKUP_DIR/$NAME" "$BACKUP_COPY_DIR/.$NAME.partial"
  (cd "$BACKUP_COPY_DIR/.$NAME.partial" && sha256sum --quiet -c SHA256SUMS)
  mv "$BACKUP_COPY_DIR/.$NAME.partial" "$BACKUP_COPY_DIR/$NAME"
  prune "$BACKUP_COPY_DIR"
fi
log "fatto"

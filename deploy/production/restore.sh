#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 Marcello Anderlini
# Ripristino di un backup creato da backup.sh.
#
#   sudo restore.sh --test /var/backups/vedettavip/vedettavip-AAAAMMGG-HHMMSS
#       prova di ripristino in un database separato (vedettavip_restore_test), conteggi delle tabelle, poi lo elimina.
#       VedettaVip resta in funzione: da eseguire periodicamente per sapere che i backup funzionano davvero.
#
#   sudo restore.sh --yes /var/backups/vedettavip/vedettavip-AAAAMMGG-HHMMSS
#       ripristino vero: ferma API, Worker e Web, ricrea il database dal dump e li riavvia.
#       Tutto ciò che è successo dopo il backup (eventi, metriche, modifiche) va perso.
#       Su un server nuovo: prima deploy.sh, poi copiare vedettavip.env del backup in /opt/vedettavip (stessa password del DB),
#       rilanciare deploy.sh, quindi questo script. La CA di Caddy si ripristina a mano (vedi README).
#   Accetta anche i backup creati prima della rinomina in VedettaVip (netmap.dump, netmap.env).
set -euo pipefail

VEDETTAVIP_DIR=${VEDETTAVIP_DIR:-/opt/vedettavip}
ENV_FILE=${ENV_FILE:-$VEDETTAVIP_DIR/vedettavip.env}
DB_CONTAINER=${DB_CONTAINER:-vedettavip-db-1}
APP_CONTAINERS=${APP_CONTAINERS-vedettavip-api-1 vedettavip-worker-1 vedettavip-web-1}

MODE=${1:-}
SRC=${2:-}
DUMP=$SRC/vedettavip.dump
[ -f "$DUMP" ] || DUMP=$SRC/netmap.dump   # backup precedente alla rinomina in VedettaVip
if [[ "$MODE" != --test && "$MODE" != --yes ]] || [ ! -f "$DUMP" ]; then
  sed -n '4,15p' "$0"; exit 2
fi

set -a; . "$ENV_FILE"; set +a
# "netmap": nome storico di utente e database, mantenuto dopo la rinomina in VedettaVip
DB_USER=${POSTGRES_USER:-netmap}
DB_NAME=${POSTGRES_DB:-netmap}
log() { echo "$(date '+%F %T') $*"; }
# Niente NOTICE ("does not exist, skipping", "already exists"): restano avvisi ed errori
export PGOPTIONS='-c client_min_messages=warning'
psql_admin() { docker exec -i -e PGOPTIONS "$DB_CONTAINER" psql -v ON_ERROR_STOP=1 -qX -U "$DB_USER" -d postgres "$@"; }

(cd "$SRC" && sha256sum --quiet -c SHA256SUMS) || { log "ERRORE: checksum non validi in $SRC"; exit 1; }

TARGET=$DB_NAME
[ "$MODE" = --test ] && TARGET=vedettavip_restore_test

# Database vuoto + procedura TimescaleDB (pre/post restore). Stessa versione di TimescaleDB del backup.
restore_into() {
  psql_admin -c "DROP DATABASE IF EXISTS \"$1\" WITH (FORCE)" -c "CREATE DATABASE \"$1\" OWNER \"$DB_USER\""
  docker exec -i -e PGOPTIONS "$DB_CONTAINER" psql -v ON_ERROR_STOP=1 -qX -U "$DB_USER" -d "$1" \
    -c "CREATE EXTENSION IF NOT EXISTS timescaledb" -c "SELECT timescaledb_pre_restore()" >/dev/null
  # Gli avvisi su oggetti già presenti (estensione, schemi di TimescaleDB) sono attesi: si controllano i dati dopo
  docker exec -i "$DB_CONTAINER" pg_restore -U "$DB_USER" -d "$1" --no-owner < "$DUMP" 2>&1 \
    | grep -vE 'already exists|timescaledb|^pg_restore: (warning|detail|hint)|errors ignored on restore' || true
  docker exec -i "$DB_CONTAINER" psql -v ON_ERROR_STOP=1 -qX -U "$DB_USER" -d "$1" -c "SELECT timescaledb_post_restore()" >/dev/null
}

counts() {
  docker exec -i "$DB_CONTAINER" psql -v ON_ERROR_STOP=1 -qX -U "$DB_USER" -d "$1" -At -F ' ' <<'SQL'
select 'dispositivi', count(*) from "Devices"
union all select 'mappe', count(*) from "Maps"
union all select 'utenti', count(*) from "AspNetUsers"
union all select 'eventi', count(*) from "Events"
union all select 'metriche', count(*) from "Metrics"
union all select 'migration', count(*) from "__EFMigrationsHistory";
SQL
}

if [ "$MODE" = --test ]; then
  log "prova di ripristino di $SRC in $TARGET"
  restore_into "$TARGET"
  counts "$TARGET" | sed 's/^/    /'
  [ "$(counts "$TARGET" | awk '$1=="migration"{print $2}')" -gt 0 ] || { log "ERRORE: database ripristinato vuoto"; exit 1; }
  psql_admin -c "DROP DATABASE \"$TARGET\" WITH (FORCE)"
  log "prova riuscita, $TARGET eliminato"
  exit 0
fi

log "ripristino di $SRC su $TARGET: stop di $APP_CONTAINERS"
# shellcheck disable=SC2086
[ -n "$APP_CONTAINERS" ] && docker stop $APP_CONTAINERS >/dev/null
restore_into "$TARGET"
counts "$TARGET" | sed 's/^/    /'
# shellcheck disable=SC2086
[ -n "$APP_CONTAINERS" ] && docker start $APP_CONTAINERS >/dev/null
log "ripristino completato, servizi riavviati"

#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 Marcello Anderlini
# dev.sh — avvia database, API, Worker e Web in un solo terminale.
# Ctrl+C ferma tutti i processi.
# Database: deploy/docker-compose.dev.yml con deploy/.env.dev (da deploy/.env.dev.example) e, se esiste,
# deploy/docker-compose.dev.override.yml (personalizzazioni locali, ignorato da Git).
set -euo pipefail
cd "$(dirname "$0")"

[ -f deploy/.env.dev ] || { echo "Manca deploy/.env.dev: cp deploy/.env.dev.example deploy/.env.dev e cambiare la password"; exit 1; }
COMPOSE=(docker compose -f deploy/docker-compose.dev.yml)
[ -f deploy/docker-compose.dev.override.yml ] && COMPOSE+=(-f deploy/docker-compose.dev.override.yml)
COMPOSE+=(--env-file deploy/.env.dev)

# Volume creato prima della separazione dei compose: senza override un database nuovo e vuoto lo nasconderebbe
if docker volume inspect netmap-db-data >/dev/null 2>&1 && ! "${COMPOSE[@]}" config | grep -q 'name: netmap-db-data$'; then
  echo "Esiste il volume netmap-db-data ma il compose di sviluppo non lo usa. Creare deploy/docker-compose.dev.override.yml con:"
  echo "  volumes: { db-data: { name: netmap-db-data, external: true } }"
  exit 1
fi

# Una tantum: il container vedettavip-db creato quando il compose di sviluppo era nel progetto "vedettavip" va
# ricreato nel progetto "vedettavip-dev" (il volume dei dati resta e viene riusato)
if [ "$(docker inspect -f '{{index .Config.Labels "com.docker.compose.project"}}' vedettavip-db 2>/dev/null || true)" = vedettavip ]; then
  echo "==> Container vedettavip-db del vecchio progetto compose: lo ricreo (i dati restano nel volume)"
  docker rm -f vedettavip-db >/dev/null
fi

echo "==> Database (attendo che sia healthy)"
"${COMPOSE[@]}" up -d --wait

echo "==> Build della solution"
dotnet build --nologo -v q

# Avvia un progetto in background con un prefisso colorato su ogni riga di log
run() {
  local name=$1 color=$2
  shift 2
  local prefix
  prefix=$(printf '\033[%sm[%s]\033[0m ' "$color" "$name")
  dotnet run --no-build "$@" 2>&1 | sed -u "s/^/${prefix}/" &
}

# Ctrl+C o kill: termina l'intero gruppo di processi (script + dotnet + sed)
trap 'echo; echo "==> Arresto in corso..."; kill 0' INT TERM

run api    36 --project src/VedettaVip.Api    --launch-profile http
run worker 33 --project src/VedettaVip.Worker
run web    35 --project src/VedettaVip.Web    --launch-profile http

echo "==> Avviati. Mappa: http://localhost:5032/map  (Ctrl+C per fermare)"
wait

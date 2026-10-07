#!/usr/bin/env bash
# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 Marcello Anderlini
# Deploy di VedettaVip su un server Debian con Docker: copia del codice, configurazione, build e avvio.
#
# Uso dalla radice del repository:
#   deploy/production/deploy.sh [--config <cartella>] [host-ssh]
#
#   host-ssh            host o alias SSH del server (utente con sudo senza password); se manca si usa
#                       VEDETTAVIP_SSH_HOST (variabile d'ambiente o .env della cartella di --config).
#   --config <cartella> configurazione dell'installazione tenuta fuori da questo repository (es. un repository privato):
#                         <cartella>/.env                        → /opt/vedettavip/vedettavip.env (sostituito a ogni deploy)
#                         <cartella>/docker-compose.override.yml → /opt/vedettavip/docker-compose.override.yml (facoltativo)
#                       Senza --config il .env viene generato sul server al primo deploy (password e chiave casuali) e
#                       poi lasciato com'è; un override già presente sul server resta in uso.
#
# Sul server: codice in /opt/vedettavip/src, configurazione in /opt/vedettavip (root, 600), comandi compose con
# /opt/vedettavip/compose.sh (es. sudo /opt/vedettavip/compose.sh ps).
set -euo pipefail
REPO=$(cd "$(dirname "$0")/../.." && pwd)

usage() { sed -n '4,18p' "$0"; exit 2; }

CONFIG=
HOST=
while [ $# -gt 0 ]; do
  case $1 in
    --config) [ $# -ge 2 ] || usage; CONFIG=${2%/}; shift 2 ;;
    -h|--help) usage ;;
    -*) echo "Opzione sconosciuta: $1" >&2; usage ;;
    *) [ -z "$HOST" ] || usage; HOST=$1; shift ;;
  esac
done

# --config relativo alla cartella da cui si lancia lo script; poi si lavora dalla radice del repository
if [ -n "$CONFIG" ]; then
  [ -d "$CONFIG" ] || { echo "Cartella di configurazione inesistente: $CONFIG" >&2; exit 1; }
  CONFIG=$(cd "$CONFIG" && pwd)
fi
cd "$REPO"

# Valore di una variabile in un file .env (ultima occorrenza, senza apici)
env_value() { sed -n "s/^$1=//p" "$2" | tail -1 | sed -e 's/^"\(.*\)"$/\1/' -e "s/^'\(.*\)'\$/\1/"; }

if [ -n "$CONFIG" ]; then
  ENV_SRC=$CONFIG/.env
  [ -f "$ENV_SRC" ] || { echo "Manca $ENV_SRC" >&2; exit 1; }
  # Controlli prima di toccare il server: un .env incompleto fermerebbe l'installazione
  for var in VEDETTAVIP_HOST POSTGRES_USER POSTGRES_DB POSTGRES_PASSWORD AGENT_API_KEY; do
    value=$(env_value "$var" "$ENV_SRC")
    [ -n "$value" ] || { echo "$ENV_SRC: $var mancante o vuota" >&2; exit 1; }
    case $value in cambiami*|*__*__*) echo "$ENV_SRC: $var ha ancora il valore di esempio" >&2; exit 1 ;; esac
  done
  if grep -qE '^[A-Za-z_]+=__[A-Za-z_]+__$' "$ENV_SRC"; then
    echo "$ENV_SRC: valori ancora da compilare:" >&2; grep -E '^[A-Za-z_]+=__[A-Za-z_]+__$' "$ENV_SRC" | cut -d= -f1 >&2; exit 1
  fi
  [ "$(env_value AGENT_API_KEY "$ENV_SRC" | wc -c)" -gt 32 ] || { echo "$ENV_SRC: AGENT_API_KEY deve avere almeno 32 caratteri" >&2; exit 1; }
  [ -n "$HOST" ] || HOST=${VEDETTAVIP_SSH_HOST:-$(env_value VEDETTAVIP_SSH_HOST "$ENV_SRC")}
fi
HOST=${HOST:-${VEDETTAVIP_SSH_HOST:-}}
[ -n "$HOST" ] || { echo "Host SSH non indicato (argomento o VEDETTAVIP_SSH_HOST)." >&2; usage; }

DIR=/opt/vedettavip

# Una tantum, sui server installati quando il progetto si chiamava NetMap (/opt/netmap, progetto compose "netmap"):
# ferma i vecchi container (i volumi restano: il compose li riusa per nome), sposta cartella e segreti, rinomina
# NETMAP_HOST e sostituisce il timer dei backup. Non fa nulla se /opt/vedettavip esiste già.
echo "==> Migrazione da NetMap (solo se serve)"
ssh "$HOST" "sudo bash -s" <<REMOTE
set -euo pipefail
OLD=/opt/netmap
if [ -d "\$OLD" ] && [ ! -e $DIR ]; then
  echo "trovata un'installazione NetMap in \$OLD: migrazione in $DIR"
  systemctl disable --now netmap-backup.timer 2>/dev/null || true
  rm -f /etc/systemd/system/netmap-backup.service /etc/systemd/system/netmap-backup.timer
  systemctl daemon-reload
  # down senza -v: i volumi (database, chiavi, CA di Caddy) non vengono toccati
  docker compose -f "\$OLD/src/deploy/production/docker-compose.yml" --env-file "\$OLD/netmap.env" down
  mv "\$OLD" $DIR
  mv $DIR/netmap.env $DIR/vedettavip.env
  sed -i -e 's/^NETMAP_HOST=/VEDETTAVIP_HOST=/' \
         -e 's|^BACKUP_DIR=/var/backups/netmap\$|BACKUP_DIR=/var/backups/vedettavip|' $DIR/vedettavip.env
  if [ -d /var/backups/netmap ] && [ ! -e /var/backups/vedettavip ]; then
    mv /var/backups/netmap /var/backups/vedettavip
  fi
  echo "migrazione completata"
fi
REMOTE

if [ -n "$CONFIG" ]; then
  # La password del DB vale solo alla creazione del volume: cambiarla nel .env renderebbe il database irraggiungibile
  echo "==> Controllo della password del database"
  LOCAL_PW=$(env_value POSTGRES_PASSWORD "$ENV_SRC" | sha256sum | cut -d' ' -f1)
  ssh "$HOST" "sudo bash -s" <<REMOTE
set -euo pipefail
if [ -f $DIR/vedettavip.env ]; then
  REMOTE_PW=\$(sed -n 's/^POSTGRES_PASSWORD=//p' $DIR/vedettavip.env | tail -1 | sed -e 's/^"\(.*\)"\$/\1/' -e "s/^'\(.*\)'\\\$/\1/" | sha256sum | cut -d' ' -f1)
  if [ "\$REMOTE_PW" != "$LOCAL_PW" ]; then
    echo "ERRORE: POSTGRES_PASSWORD di $ENV_SRC è diversa da quella in uso sul server ($DIR/vedettavip.env)." >&2
    echo "Copiare quella del server nel .env di configurazione (o cambiarla prima nel database con ALTER USER)." >&2
    exit 1
  fi
fi
REMOTE
fi

echo "==> Copia del codice su $HOST:$DIR/src (file del repository, senza segreti né build locali)"
git ls-files -z --cached --others --exclude-standard \
  | grep -zvE '(^|/)\.env(\.(dev|local|production))?$|override\.yml$|\.Local\.json$' \
  | tar --null -T - -czf - \
  | ssh "$HOST" "sudo rm -rf $DIR/src.new && sudo mkdir -p $DIR/src.new && sudo tar -xzf - -C $DIR/src.new --no-same-owner \
                 && sudo chmod -R go-w $DIR/src.new \
                 && sudo rm -rf $DIR/src && sudo mv $DIR/src.new $DIR/src"

if [ -n "$CONFIG" ]; then
  echo "==> Configurazione da $CONFIG"
  ssh "$HOST" "sudo install -m 600 -o root -g root /dev/stdin $DIR/vedettavip.env" < "$ENV_SRC"
  if [ -f "$CONFIG/docker-compose.override.yml" ]; then
    ssh "$HOST" "sudo install -m 600 -o root -g root /dev/stdin $DIR/docker-compose.override.yml" < "$CONFIG/docker-compose.override.yml"
  else
    ssh "$HOST" "sudo rm -f $DIR/docker-compose.override.yml"
  fi
else
  echo "==> Segreti (generati solo se mancano)"
  ssh "$HOST" "sudo bash -s" <<REMOTE
set -euo pipefail
ENV=$DIR/vedettavip.env
if [ ! -f "\$ENV" ]; then
  umask 077
  sed -e "s|^POSTGRES_PASSWORD=.*|POSTGRES_PASSWORD=\$(openssl rand -hex 24)|" \
      -e "s|^AGENT_API_KEY=.*|AGENT_API_KEY=\$(openssl rand -base64 36 | tr -d '\n')|" \
      $DIR/src/deploy/.env.example > "\$ENV"
  echo "creato \$ENV (controllare VEDETTAVIP_HOST)"
fi
chmod 600 "\$ENV"
REMOTE
fi

# compose.sh: lo stack con il .env e l'eventuale override del server, usato qui, dal README e a mano
echo "==> $DIR/compose.sh"
ssh "$HOST" "sudo bash -s" <<'REMOTE'
set -euo pipefail
cat > /opt/vedettavip/compose.sh <<'EOF'
#!/usr/bin/env bash
# Generato da deploy.sh: docker compose dello stack VedettaVip con la configurazione di questo server.
set -euo pipefail
D=/opt/vedettavip
F=(-f "$D/src/deploy/docker-compose.yml")
[ -f "$D/docker-compose.override.yml" ] && F+=(-f "$D/docker-compose.override.yml")
exec docker compose "${F[@]}" --env-file "$D/vedettavip.env" "$@"
EOF
chmod 700 /opt/vedettavip/compose.sh
REMOTE
COMPOSE="sudo $DIR/compose.sh"

# Volumi creati con altri nomi (es. netmap_* prima della rinomina) e non usati dalla configurazione: senza un override
# che li indichi, Docker creerebbe volumi nuovi e vuoti e VedettaVip ripartirebbe da zero.
echo "==> Controllo dei volumi"
ssh "$HOST" "sudo bash -s" <<'REMOTE'
set -euo pipefail
CONFIG=$(/opt/vedettavip/compose.sh config)
for v in $(docker volume ls -q | grep -E '^netmap_(db-data|web-keys|caddy-data|caddy-config)$' || true); do
  if ! grep -qE "name: $v\$" <<<"$CONFIG"; then
    echo "ERRORE: il volume $v esiste ma la configurazione non lo usa." >&2
    echo "Aggiungere in docker-compose.override.yml:  volumes: { ${v#netmap_}: { name: $v, external: true } }" >&2
    exit 1
  fi
done
REMOTE

echo "==> Build e avvio"
ssh "$HOST" "$COMPOSE up -d --build --remove-orphans"
# Il Caddyfile è montato dalla cartella del codice appena sostituita: riavvio per rileggerlo
ssh "$HOST" "$COMPOSE restart caddy"
ssh "$HOST" "$COMPOSE ps"

# Ogni build lascia la sua cache (in poche settimane decine di GB): si tiene solo quella degli ultimi 7 giorni,
# sufficiente per deploy ravvicinati veloci. Immagini in uso e volumi non vengono toccati.
echo "==> Pulizia della cache di build più vecchia di 7 giorni"
ssh "$HOST" "sudo docker builder prune -f --filter until=168h | tail -1"

echo "==> Backup notturno (timer systemd)"
ssh "$HOST" "sudo install -m 644 $DIR/src/deploy/production/vedettavip-backup.service $DIR/src/deploy/production/vedettavip-backup.timer \
               /etc/systemd/system/ && sudo systemctl daemon-reload && sudo systemctl enable --now vedettavip-backup.timer \
             && systemctl list-timers vedettavip-backup.timer --no-pager | head -2"

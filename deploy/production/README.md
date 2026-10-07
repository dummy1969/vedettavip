# VedettaVip in produzione

Un server Debian con Docker; VedettaVip raggiungibile dalla rete interna su `https://<VEDETTAVIP_HOST>` (Caddy, certificato
della CA interna). Solo Caddy espone porte (80/443); database, API, Worker e Web stanno sulla rete interna di Docker.
Lo stack è `deploy/docker-compose.yml` (con `deploy/Caddyfile`); questa cartella contiene gli script per il server.

| Servizio | Immagine | Note |
|---|---|---|
| `db` | `timescale/timescaledb:2.30.2-pg17` | versione **fissata**: deve coincidere con quella dei backup da ripristinare |
| `migrate` | `vedettavip-migrator` (bundle EF) | applica le migration e termina; l'API parte solo se riesce |
| `api` | `vedettavip-api` | `ASPNETCORE_FORWARDEDHEADERS_ENABLED`, cookie Secure |
| `worker` | `vedettavip-worker` | non root, solo `CAP_NET_RAW` (sul binario `dotnet`) per il ping |
| `web` | `vedettavip-web` | Blazor (pagine + WebAssembly) |
| `caddy` | `caddy:2.11.6` | HTTPS `tls internal`, `/api` e `/hubs` → API, il resto → Web |

Sul server (`/opt/vedettavip`, root, 750):

| Percorso | Contenuto |
|---|---|
| `src/` | copia del repository (sostituita a ogni deploy) |
| `vedettavip.env` | configurazione e segreti (600): modello in `deploy/.env.example` |
| `docker-compose.override.yml` | facoltativo: personalizzazioni dello stack (es. nomi dei volumi) |
| `compose.sh` | `docker compose` con i file sopra, generato da `deploy.sh` |

Dati nei volumi Docker `vedettavip_db-data`, `vedettavip_web-keys`, `vedettavip_caddy-data`, `vedettavip_caddy-config`.

## Configurazione fuori dal repository

Tutto ciò che riguarda una specifica installazione (indirizzo, password, chiavi, NAS, nomi dei volumi) sta fuori dal
codice, per esempio in un repository privato con:

```
vedettavip-deploy/
├── .env                        # da deploy/.env.example, con VEDETTAVIP_SSH_HOST e i segreti veri
├── docker-compose.override.yml # facoltativo
└── appsettings.Local.json      # facoltativo, da montare nei container con l'override
```

Uso con `deploy.sh` (copia `.env` e override sul server, poi build e avvio):

```bash
<vedettavip>/deploy/production/deploy.sh --config <vedettavip-deploy>
```

oppure direttamente con Docker Compose, sulla macchina che esegue lo stack:

```bash
docker compose -f <vedettavip>/deploy/docker-compose.yml -f docker-compose.override.yml --env-file .env up -d --build
```

Nell'override i percorsi relativi partono da `<vedettavip>/deploy`. Esempio con i volumi di un'installazione nata quando
il progetto si chiamava NetMap e un `appsettings.Local.json` per l'API:

```yaml
services:
  api:
    volumes:
      - /opt/vedettavip/appsettings.Local.json:/app/appsettings.Local.json:ro
volumes:
  db-data: { name: netmap_db-data, external: true }
  web-keys: { name: netmap_web-keys, external: true }
  caddy-data: { name: netmap_caddy-data, external: true }
  caddy-config: { name: netmap_caddy-config, external: true }
```

Controlli di `deploy.sh --config`: nessun valore d'esempio nel `.env`, `AGENT_API_KEY` di almeno 32 caratteri,
`POSTGRES_PASSWORD` uguale a quella già in uso sul server (vale solo alla creazione del volume), e nessun volume
`netmap_*` esistente lasciato fuori dalla configurazione (Docker creerebbe volumi nuovi e vuoti).

## Primo deploy (dal PC di sviluppo, host SSH con sudo senza password)

```bash
ssh <host-ssh> 'sudo bash -s' < deploy/production/setup-server.sh   # Docker, firewall 22/80/443
deploy/production/deploy.sh --config <vedettavip-deploy>            # oppure: deploy.sh <host-ssh> (segreti generati)
```

Senza `--config`, al primo deploy `vedettavip.env` viene generato dal modello con password e chiave casuali; controllare
poi `VEDETTAVIP_HOST` e rilanciare il deploy.

Al primo avvio con database vuoto il log dell'API contiene il **codice di setup** del primo amministratore:

```bash
ssh <host-ssh> 'sudo /opt/vedettavip/compose.sh logs api | grep "Codice di setup"'
```

## Aggiornamento

```bash
deploy/production/deploy.sh --config <vedettavip-deploy>     # oppure deploy.sh <host-ssh>
```

Copia i file del repository (tracciati e non ignorati, mai `.env`, override, `appsettings.*Local.json`, `bin`, `obj`),
ricostruisce le immagini cambiate, applica le migration (container `migrate`) e riavvia Caddy (il `Caddyfile` è montato
dalla cartella sostituita). Alla fine elimina la cache di build di Docker più vecchia di 7 giorni: senza pulizia cresce
a ogni deploy (oltre 40 GB in poche settimane). Per svuotarla tutta: `sudo docker builder prune -f` (il deploy
successivo ricostruisce da zero, più lento).

## Migrazione di un server installato come NetMap

`deploy.sh` se trova `/opt/netmap` e non `/opt/vedettavip`, prima della copia del codice:

1. disattiva e rimuove `netmap-backup.timer`/`.service`;
2. ferma il vecchio progetto compose `netmap` con `down` **senza `-v`** (i volumi restano);
3. sposta `/opt/netmap` → `/opt/vedettavip` e `netmap.env` → `vedettavip.env`, rinominando `NETMAP_HOST` in
   `VEDETTAVIP_HOST` e `BACKUP_DIR=/var/backups/netmap` in `/var/backups/vedettavip` (cartella spostata).

I volumi di quell'installazione si chiamano `netmap_*`: vanno indicati nell'override (esempio sopra), altrimenti
`deploy.sh` si ferma. Il montaggio del NAS creato dalla vecchia versione di `setup-nas.sh` continua a funzionare;
per i nuovi nomi rilanciare `setup-nas.sh` e togliere a mano la vecchia riga da `/etc/fstab`. I backup `netmap-*`
precedenti restano ripristinabili con `restore.sh` e scadono con la stessa conservazione.

Verifica dopo il deploy:

```bash
ssh <host-ssh> 'sudo docker ps --format "{{.Names}} {{.Status}}"; sudo docker volume ls; systemctl list-timers vedettavip-backup.timer --no-pager'
```

## Comandi utili (sul server)

```bash
C="sudo /opt/vedettavip/compose.sh"
$C ps                       # stato
$C logs -f --tail 100 api   # log (api, worker, web, caddy, db, migrate)
$C restart worker
$C exec db psql -U netmap -d netmap
```

## Backup e ripristino

**Automatico**: `deploy.sh` installa `vedettavip-backup.timer` (systemd), che ogni notte alle 02:30 (o all'avvio, se la VM
era spenta) lancia `backup.sh`. Ogni backup è una cartella `/var/backups/vedettavip/vedettavip-AAAAMMGG-HHMMSS/` (700, root):

| File | Contenuto |
|---|---|
| `vedettavip.dump` | `pg_dump -Fc` verificato con `pg_restore --list`: configurazione, eventi, metriche, utenti, chiavi di Data Protection |
| `vedettavip.env` | password del DB, chiave degli agenti |
| `caddy-data.tar.gz` | CA interna di Caddy e certificati |
| `SHA256SUMS` | controllati da `restore.sh` e dopo la copia esterna |

**Copia su NAS SMB** (consigliata): sul server, in un terminale (chiede la password, che resta solo in
`/root/.vedettavip-nas.cred`), `sudo /opt/vedettavip/src/deploy/production/setup-nas.sh //nas.example.lan/backup backup-user`.
Installa cifs-utils, monta la share su `/mnt/vedettavip-nas` (automount systemd, `nofail`), imposta `BACKUP_COPY_DIR` e
prova un backup. Con `deploy.sh --config` mettere `BACKUP_COPY_DIR` anche nel `.env` di configurazione: il deploy
sostituisce `vedettavip.env`.

Impostazioni in `vedettavip.env` (facoltative): `BACKUP_DIR` (default `/var/backups/vedettavip`),
`BACKUP_KEEP_DAYS` (14), `BACKUP_COPY_DIR` = copia su un **NAS già montato** (es. voce in `/etc/fstab` per una share
NFS/SMB su `/mnt/nas`, poi `BACKUP_COPY_DIR=/mnt/nas/vedettavip`). Se il NAS non è montato il backup locale resta e il
servizio termina in errore, così non si scrive per sbaglio sul disco della VM.
**Senza copia esterna il backup non protegge dalla perdita della VM.**

```bash
systemctl list-timers vedettavip-backup.timer                 # prossima esecuzione
sudo systemctl start vedettavip-backup.service                # backup subito
journalctl -u vedettavip-backup.service -n 50                 # esito degli ultimi backup
sudo ls -l /var/backups/vedettavip

# Prova di ripristino (database separato, poi eliminato; VedettaVip resta attivo): da fare almeno una volta al mese
sudo /opt/vedettavip/src/deploy/production/restore.sh --test /var/backups/vedettavip/vedettavip-AAAAMMGG-HHMMSS

# Ripristino vero: ferma api/worker/web, ricrea il database dal dump, li riavvia (si perde quanto avvenuto dopo il backup)
sudo /opt/vedettavip/src/deploy/production/restore.sh --yes /var/backups/vedettavip/vedettavip-AAAAMMGG-HHMMSS
```

**Su un server nuovo**: `setup-server.sh`, poi copiare `vedettavip.env` del backup (`netmap.env` nei backup creati
prima della rinomina; rinominare `NETMAP_HOST` in `VEDETTAVIP_HOST`) in `/opt/vedettavip/vedettavip.env` (600, root:
la password del DB deve coincidere) o nel `.env` di configurazione, `deploy.sh`, quindi `restore.sh --yes`. Per tenere
la stessa CA (i client non vanno riconfigurati), prima di avviare Caddy (volume `vedettavip_caddy-data`, o il nome
fissato nell'override):

```bash
$C stop caddy
sudo docker run --rm -v vedettavip_caddy-data:/data -v /var/backups/vedettavip/vedettavip-AAAAMMGG-HHMMSS:/b:ro \
  caddy:2.11.6 sh -c 'rm -rf /data/* && tar -xzf /b/caddy-data.tar.gz -C /data'
$C start caddy
```

Il dump va ripristinato con la **stessa versione di TimescaleDB** (immagine fissata nel compose). Le chiavi di Data
Protection (cifrano password SMTP, token Telegram e cookie di sessione) sono nel database: chi ha il backup può
decifrare quei segreti, quindi va protetto come una password.

## Certificato HTTPS

Caddy usa una propria CA interna (radice valida 10 anni, certificati del sito rinnovati automaticamente). Per evitare
l'avviso del browser, importare la radice nei client:

```bash
ssh <host-ssh> "sudo /opt/vedettavip/compose.sh exec -T caddy cat /data/caddy/pki/authorities/local/root.crt" > vedettavip-ca-root.crt
```

- Windows (PowerShell da amministratore): `Import-Certificate -FilePath vedettavip-ca-root.crt -CertStoreLocation Cert:\LocalMachine\Root`
- Linux (Chrome/Chromium): Impostazioni → Privacy e sicurezza → Sicurezza → Gestisci certificati → Autorità → Importa
- Firefox: Impostazioni → Privacy e sicurezza → Certificati → Mostra certificati → Autorità → Importa

## Insidie

- **Accesso per IP e SNI**: collegandosi a un IP i client non inviano il nome del server; senza `default_sni` nel
  `Caddyfile` l'handshake TLS fallisce (curl: codice 000, browser: errore di connessione).
- **Ping nel container**: .NET usa socket ICMP raw e, se non può, il comando `ping` (assente nell'immagine). Il
  Dockerfile del Worker dà `CAP_NET_RAW` al binario `dotnet` con `setcap`; il compose toglie tutte le altre capability.
- **IP del server**: con DHCP conviene una prenotazione sul router (o IP statico): `VEDETTAVIP_HOST` e il certificato
  dipendono dall'indirizzo.
- **Sviluppo e produzione insieme**: se il database di sviluppo è una copia di quello di produzione, entrambi
  inviano le notifiche agli stessi contatti; disattivare le email nell'ambiente di sviluppo.

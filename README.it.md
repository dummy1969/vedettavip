# VedettaVip

*[Read in English](README.md)*

VedettaVip è un sistema di monitoraggio di rete costruito attorno a una **mappa topologica live**, ispirato a MikroTik
The Dude: nodi colorati in base allo stato, link con il traffico in tempo reale, sottomappe e configurazione con il
drag & drop. Si integra a fondo con MikroTik RouterOS (API, vicini, risorse, peer WireGuard) e controlla qualsiasi
apparato con ICMP e SNMP.

Non vuole sostituire strumenti generalisti come Zabbix o LibreNMS: il centro è una mappa piacevole da guardare sullo
schermo di un NOC e veloce da configurare.

![Mappa dei dati dimostrativi: nodi colorati per stato, link, una sottomappa e il nodo Internet](docs/images/map.png)

*Lo screenshot mostra solo i dati dimostrativi creati dalla prima migration del database.*

> L'interfaccia e i messaggi di log sono, per ora, **in italiano**.

## Funzionalità

- **Mappa topologica** (SVG scritto a mano, Blazor WebAssembly): Up verde, Partial arancione, Down rosso, Unknown
  grigio; etichette a template con valori live (`[Name]`, `[Address]`, `[Cpu]`, `[Temp]`, `[Uptime]`…); link disegnati
  come due metà tx/rx, colore e spessore in base all'utilizzo; snap alla griglia, pan, zoom; modalità sola
  visualizzazione per il NOC e modalità modifica (nodi aggiunti con il clic destro, link tracciati trascinando).
- **Sottomappe** con stato aggregato (il peggiore di tutte le discendenti, aggiornato dal vivo) e badge riassuntivo.
- **Agente di polling** (processo separato, senza accesso al database): ICMP con isteresi, SNMP v1/v2c, traffico delle
  interfacce dai contatori a 64 bit (ripiego a 32 bit per gli apparati senza `ifXTable`), inventario delle interfacce,
  soglie configurabili.
- **RouterOS**: API classica (`api`/`api-ssl`, v6 e v7) per CPU, memoria, temperatura, tensione, uptime, versione;
  interfacce e peer WireGuard sorvegliati; soglie di CPU e temperatura.
- **Scoperta**: vicini (RouterOS `/ip/neighbor`, LLDP-MIB, CDP-MIB), tabelle ARP con i lease DHCP, scansione di subnet
  (ping, DNS, SNMP, porte TCP), produttore dal MAC address (OUI IEEE); proposte da confermare, mai aggiunte da sole.
- **Metriche** su TimescaleDB (dati grezzi 7 giorni, aggregati a 5 minuti 90 giorni, orari 2 anni) con grafici di
  traffico, latenza/perdita e RouterOS.
- **Notifiche** via email (SMTP) e Telegram: destinatari per cliente, mappa o tutto; dipendenze padre/figlio contro le
  tempeste di avvisi; soppressione dei flap; finestre di manutenzione (singole e settimanali); promemoria; presa in carico.
- **Utenti e ruoli** (Admin, Operatore, Lettura), account locali, protezione CSRF, segreti cifrati con ASP.NET Core
  Data Protection.
- **Gestione dei dispositivi**: pagina riepilogativa con filtri, import/export CSV.

## Avvio rapido (Docker Compose)

Requisiti: un host Linux con Docker Engine e il plugin Compose.

```bash
git clone https://github.com/dummy1969/vedettavip.git
cd vedettavip
cp deploy/.env.example deploy/.env
chmod 600 deploy/.env
```

Modificare `deploy/.env`:

- `VEDETTAVIP_HOST`: l'indirizzo IP o il nome DNS con cui si aprirà VedettaVip (`localhost` per una prova sullo stesso
  computer);
- `POSTGRES_PASSWORD` e `AGENT_API_KEY`: sostituire i valori di esempio, ad esempio con `openssl rand -hex 24` e
  `openssl rand -base64 36`.

Poi:

```bash
docker compose -f deploy/docker-compose.yml up -d --build
# codice di setup del primo amministratore, scritto nel log dell'API
docker compose -f deploy/docker-compose.yml logs api | grep "Codice di setup"
```

Aprire `https://<VEDETTAVIP_HOST>`, accettare il certificato (emesso dalla CA interna di Caddy: per evitare l'avviso
importarne la radice, vedi [deploy/production/README.md](deploy/production/README.md)) e creare l'amministratore con il
codice di setup. La prima mappa contiene dispositivi dimostrativi con indirizzi fittizi: sostituirli con i propri nella
pagina **Dispositivi**.

Per un server di produzione (deploy via SSH, backup notturni, ripristino, copia su NAS) vedi
[deploy/production/README.md](deploy/production/README.md).

## Configurazione

Tutti i valori specifici di un'installazione arrivano da fuori dal repository: non serve modificare il codice.

| Dove | Cosa |
|---|---|
| `deploy/.env` (da `deploy/.env.example`) | nome dell'host, porte (`HTTP_PORT`, `HTTPS_PORT`), credenziali del database, chiave e nome dell'agente, community SNMP di ripiego, fuso orario, `APP_SOURCE_URL`, impostazioni dei backup |
| `docker-compose.override.yml` | tutto il resto dello stack: nomi dei volumi, mount aggiuntivi, limiti di risorse |
| `appsettings.Local.json`, `appsettings.{Environment}.Local.json` | impostazioni .NET di API, Worker o Web, accanto al loro `appsettings.json` (caricate dopo di esso, prima delle variabili d'ambiente) |
| Interfaccia web → Impostazioni | profili SNMP e RouterOS, soglie di rilevazione e delle metriche, SMTP, Telegram, contatti, clienti, utenti |

Questi file sono ignorati da Git (`.gitignore`) e dal contesto di build di Docker (`.dockerignore`). Una soluzione
tipica è tenerli in un repository privato separato e applicarli sopra il codice pubblico:

```bash
docker compose -f <vedettavip>/deploy/docker-compose.yml -f docker-compose.override.yml --env-file .env up -d --build
```

I percorsi relativi dell'override partono da `<vedettavip>/deploy`. Impostazioni principali:

| Variabile | Default | Note |
|---|---|---|
| `VEDETTAVIP_HOST` | – (obbligatoria) | usata da Caddy per il certificato HTTPS |
| `POSTGRES_USER`, `POSTGRES_DB`, `POSTGRES_PASSWORD` | `netmap`, `netmap`, – | valgono solo alla creazione del volume del database |
| `AGENT_API_KEY` | – (obbligatoria, ≥ 32 caratteri) | condivisa tra API e agenti di polling (`X-Agent-Key`) |
| `AGENT_ID` | `vedettavip-server` | nome dell'agente che gira nello stack |
| `SNMP_COMMUNITY` | `public` | ripiego se nell'interfaccia non c'è un profilo SNMP |
| `APP_SOURCE_URL` | repository ufficiale | link "Source code" dell'interfaccia (vedi Licenza) |
| `TZ` | `Europe/Rome` | |

Le credenziali salvate dall'interfaccia (community SNMP, password RouterOS, password SMTP, token Telegram) sono cifrate
con ASP.NET Core Data Protection; le chiavi stanno nel database, quindi **i backup del database vanno trattati come
segreti**.

## Architettura

```
Browser (Blazor WebAssembly: mappa, dashboard) ◄── SignalR / REST ──► VedettaVip.Api ──► PostgreSQL + TimescaleDB
                                                                          ▲
                                                       HTTPS (X-Agent-Key) │ SignalR /hubs/agent
                                                                          │
                                                     VedettaVip.Worker (agente di polling: ICMP, SNMP,
                                                     API RouterOS, scoperta, scansioni di subnet)
```

| Progetto | Ruolo |
|---|---|
| `VedettaVip.Api` | minimal API ASP.NET Core, hub SignalR, EF Core (Npgsql), notifiche, soglie, pianificatore della scoperta |
| `VedettaVip.Worker` | agente di polling; parla con l'API solo via HTTP, mai con il database (pronto a diventare un agente remoto) |
| `VedettaVip.Web` / `VedettaVip.Web.Client` | host Blazor Web App e componenti WebAssembly (mappa, pagine) |
| `VedettaVip.Shared` | DTO, enum, template delle etichette condivisi da client e server |

Stack: .NET 10, ASP.NET Core, Blazor, SignalR, EF Core, PostgreSQL 17 + TimescaleDB, Lextm.SharpSnmpLib, MailKit,
Blazor-ApexCharts, Caddy. In Docker il Worker ha bisogno solo della capability `NET_RAW` (ICMP).

## Roadmap

- Agenti remoti installati nelle reti dei clienti, accesso multi-tenant per cliente (scenario MSP)
- Autenticazione a due fattori (TOTP) e accesso esterno (OIDC: Entra ID, Keycloak)
- Sfondi delle mappe (planimetrie, mappe geografiche) e auto-layout force-directed per i nodi scoperti
- Propagazione live delle modifiche della mappa agli altri browser aperti; link fra mappe diverse
- RouterOS: IPsec, PPP, EoIP/GRE, MNDP; SNMPv3
- Notifiche via webhook

## Contributi e sicurezza

Vedi [CONTRIBUTING.md](CONTRIBUTING.md) (i contributi richiedono il DCO, `git commit -s`) e [SECURITY.md](SECURITY.md)
(le vulnerabilità si segnalano in privato, non con issue pubbliche).

## Licenza

Copyright (C) 2026 Marcello Anderlini.

VedettaVip è software libero, distribuito con licenza **GNU Affero General Public License v3.0 o successiva**
([LICENSE](LICENSE)), con un termine aggiuntivo ai sensi della sezione 7(b) ([NOTICE](NOTICE)).

In breve (fa fede il testo della licenza):

- puoi usare, studiare, modificare e ridistribuire VedettaVip, anche per scopi commerciali;
- se lo distribuisci, modificato o no, devi farlo con la stessa licenza e fornire il codice sorgente corrispondente;
- se lo **modifichi e lo fai usare ad altri attraverso la rete** (ad esempio come servizio ospitato), devi offrire a quegli
  utenti il sorgente della tua versione modificata: imposta `APP_SOURCE_URL` sull'indirizzo del tuo sorgente;
- **clausola di attribuzione** (NOTICE, sezione 7(b)): le versioni modificate devono conservare l'attribuzione
  "VedettaVip – created by Marcello Anderlini" nel footer dell'interfaccia web e nella pagina About.

I componenti di terze parti e le loro licenze sono elencati in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

### Nota su TimescaleDB

Lo stack Docker usa l'immagine ufficiale `timescale/timescaledb`. VedettaVip si appoggia a funzioni di TimescaleDB
(compressione, policy di retention, continuous aggregate) distribuite con la **Timescale License (TSL)**, che non è
una licenza open source: consente l'uso interno e l'uso come backend dei propri prodotti e servizi, ma non di offrire
TimescaleDB stesso come servizio di database. TimescaleDB è un programma separato, non fa parte di VedettaVip: chi
gestisce un'installazione deve rispettarne la licenza. Vedi
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md#timescaledb-and-the-timescale-license).

### Marchi

VedettaVip is not affiliated with or endorsed by MikroTik. The Dude and RouterOS are trademarks of MikroTik.

*(VedettaVip non è affiliato né approvato da MikroTik. The Dude e RouterOS sono marchi di MikroTik.)*

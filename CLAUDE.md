# VedettaVip — Network monitor con mappa stile The Dude

## Obiettivo del progetto

Sviluppare un sistema di monitoring di rete ispirato a **MikroTik The Dude**, il cui elemento
centrale e distintivo è la **mappa topologica**: bella da vedere, interattiva, con dati live,
facile da configurare tramite drag & drop.

Zabbix e LibreNMS sono già noti e usati: NON replicarne le funzionalità generiche.
Il valore del progetto sta in:

1. Mappa visuale immediata (stato dei nodi a colori, link con traffico live, sottomappe).
2. Integrazione profonda con RouterOS (API/REST, MNDP, stato interfacce, risorse).
3. Configurazione semplice: niente form complessi per posizionare o collegare i nodi.

## Nome del progetto

Il progetto si chiamava **NetMap**; ora è **VedettaVip**. Grafia: `VedettaVip` in testi, namespace e nomi dei
progetti; `vedettavip` minuscolo in immagini Docker, servizi e container compose, URL e nomi di file.
Restano volutamente "netmap"/"NetMap" (non cambiarli):

- `SetApplicationName("NetMap")` e il purpose `NetMap.Secrets.v1` di Data Protection (`Program.cs`, `SecretProtector`):
  fanno parte della chiave, con un altro nome segreti cifrati nel DB e cookie di sessione diventano illeggibili;
- gli `UserSecretsId` di API e Worker (quello del Worker contiene `NetMap.Worker`): i segreti esistenti restano validi;
- volumi Docker: i compose pubblici usano nomi neutri (`vedettavip-dev_db-data`, `vedettavip_db-data`, …); le
  installazioni nate come NetMap li rifissano ai vecchi nomi (`netmap-db-data`, `netmap_*`) con un override locale.
  `dev.sh` e `deploy.sh` si fermano se trovano un volume `netmap*` non usato dalla configurazione;
- utente e database PostgreSQL `netmap` (`.env.example`, default di `backup.sh`/`restore.sh`);
- in `deploy/production`: la migrazione una tantum di `deploy.sh` da `/opt/netmap` e la compatibilità di
  `backup.sh`/`restore.sh` con i backup `netmap-*` creati prima della rinomina.

## Preferenze di lavoro

- Rispondi **in italiano**.
- Risposte strutturate, operative, con esempi pronti all'uso (CLI, YAML, script, config).
- Quando correggi un errore, fornisci o riscrivi il **file completo**, non diff parziali.
- Guide passo-passo verificabili per i task complessi.
- Prima di modifiche ampie, spiega brevemente il piano; poi esegui.
- Dopo ogni modifica significativa esegui `dotnet build` e verifica che compili.
- **Versione**: prima di **ogni commit** incrementa `<Version>` in `Directory.Build.props` (mostrata nella pagina
  About `/about`, voce "About" del menu): di norma la patch (0.1.1 → 0.1.2); minor o major solo quando lo chiede
  l'utente. Nel repository privato MyTheDude lo stesso commit porta la stessa versione.

## Licenza

- **AGPL-3.0-or-later** (`LICENSE`, testo integrale e non modificato da gnu.org), con il termine aggiuntivo 7(b) in
  `NOTICE`: le versioni modificate conservano l'attribuzione nel footer (`MainLayout`: "VedettaVip – © 2026 Marcello
  Anderlini – AGPL-3.0 – Source code") e nella pagina `/about` ("VedettaVip – created by Marcello Anderlini"; voce "About" del menu, con autori, versione e
  copyright).
- **Ogni nuovo file sorgente** (.cs, .razor, .razor.cs, .js, .css, .sh, Dockerfile) inizia con
  `SPDX-License-Identifier: AGPL-3.0-or-later` e `Copyright (C) 2026 Marcello Anderlini` nel commento del linguaggio
  (`//`, `@* *@`, `/* */`, `#` dopo lo shebang). Esclusi file generati (`*.g.cs`), migration EF e `wwwroot/lib`.
- Link "Source code": `App:SourceUrl` del Web host (`AppOptions`, default in `appsettings.json`), obbligo della sezione 13
  per chi offre una versione modificata in rete. Versione in `Directory.Build.props` (mostrata in `/about`).
- **Nuove dipendenze**: solo licenze compatibili con l'AGPLv3 (MIT, BSD, Apache-2.0, PostgreSQL, LGPL…), verificate sul
  pacchetto effettivo, e aggiunte a `THIRD-PARTY-NOTICES.md` e ai crediti di `/about` (`About.razor.cs`).

## Stack tecnologico

| Ambito              | Scelta                                                              |
|---------------------|---------------------------------------------------------------------|
| Runtime             | .NET 10 (LTS), C# ultima versione, nullable abilitato               |
| Backend             | ASP.NET Core Web API                                                |
| Real-time           | SignalR (hub di stato e metriche)                                   |
| Frontend            | Blazor Web App (render mode Auto/WebAssembly per la mappa)          |
| Mappa               | Componente **SVG scritto a mano** in Blazor (no librerie diagrammi) |
| Grafici             | Blazor-ApexCharts **6.0.2** (ApexCharts.js 4.7.0, MIT): non aggiornare |
| Database            | PostgreSQL + **TimescaleDB** per le metriche                        |
| ORM                 | EF Core (Npgsql)                                                    |
| SNMP                | Lextm.SharpSnmpLib (v1/v2c/v3, Get/BulkWalk, trap)                  |
| RouterOS            | tik4net (API 8728/8729) e/o REST API v7 (`/rest` su HTTPS)          |
| Scheduling          | `BackgroundService` + `PeriodicTimer` (Quartz.NET solo se serve)    |
| Test                | xUnit                                                               |
| Deploy              | Docker Compose                                                      |

## Struttura della solution

```
VedettaVip.sln
├── src/
│   ├── VedettaVip.Shared/        # Modelli, DTO, enum, LabelTemplate (condivisi client/server)
│   ├── VedettaVip.Api/           # Web API, SignalR hub, EF Core DbContext, migrations
│   ├── VedettaVip.Worker/        # Poller (ICMP/SNMP/TCP/RouterOS), Discovery, Alert engine
│   ├── VedettaVip.Web/           # Blazor Web App host (server)
│   └── VedettaVip.Web.Client/    # Componenti interattivi WebAssembly (NetworkMap.razor)
├── tests/
│   └── VedettaVip.Tests/
├── third-party/
│   ├── tabler-icons/         # icone della mappa (MIT): sottoinsieme, licenza, generate.py → MapIcons.g.cs
│   └── ieee-oui/             # registri IEEE dei MAC: generate.py → VedettaVip.Api/Resources/oui.tsv.gz
└── deploy/
    ├── docker-compose.yml      # stack completo: db, migrate, api, worker, web, Caddy (progetto "vedettavip")
    ├── Caddyfile
    ├── .env.example            # modello per deploy/.env (non committato)
    ├── docker-compose.dev.yml  # sviluppo: solo TimescaleDB, usato da dev.sh (progetto "vedettavip-dev")
    ├── .env.dev.example        # modello per deploy/.env.dev (non committato)
    └── production/             # script del server: setup-server.sh, deploy.sh, backup.sh, restore.sh, setup-nas.sh, README.md
```

**Nessun dato di un'installazione reale nel repository** (IP, nomi di macchine e router, clienti, share, segreti).
Tutto arriva da fuori, da file ignorati da Git: `deploy/.env` / `deploy/.env.dev`, `docker-compose.override.yml` /
`docker-compose.dev.override.yml`, `appsettings.Local.json` e `appsettings.{Environment}.Local.json` (caricati da
`src/LocalSettings.cs`, collegato ad API, Worker e Web, dopo gli appsettings e prima di user-secrets e variabili
d'ambiente; esclusi anche dalle immagini Docker). Nei test e negli esempi: IP RFC 5737 (`192.0.2.0/24`,
`198.51.100.0/24`, `203.0.113.0/24`) o il `10.0.x.x` del seed, MAC sintetici (OUI vero + `00:00:xx`), domini `example.*`.

Il Worker è separato dall'API fin dall'inizio: in futuro potrà diventare un **agente remoto**
(equivalente del Dude Agent) installato nelle reti dei clienti, che invia i risultati al
server centrale via HTTPS o gRPC.

## Architettura

```
Blazor (mappa SVG, dashboard) ◄── SignalR / REST ──► VedettaVip.Api
                                                        │
                       ┌────────────────────────────────┼────────────────────┐
                       │                                │                    │
                 Poller Worker                   Discovery Worker      Alert Engine
              ICMP/SNMP/TCP/RouterOS          MNDP/LLDP/ARP/scan     regole, dipendenze,
                       │                                │              notifiche
                       └──────────────► PostgreSQL + TimescaleDB ◄────────────┘
```

### Agente di polling ↔ API (implementato)

```
VedettaVip.Worker (agente)                         VedettaVip.Api                         Browser /map
 TargetProvider ─ GET /api/agent/targets ─────► Devices (Enabled)
 PollingService ogni 30 s (ICMP → SNMP)
   DeviceStateTracker (isteresi)
   → StatusPublisher ─ POST /api/agent/status ─► DeviceStatusService ─► DeviceStatuses + Events
     cambi subito + snapshot ogni 5 min            (X-Agent-Key)        └► SignalR /hubs/status ─► nodo
```

- Il Worker **non accede al database**, nemmeno in lettura: target e risultati passano solo via HTTP.
  È già la forma dell'agente remoto futuro.
- **Autenticazione agent**: header `X-Agent-Key`, chiave in `Agent:ApiKey` (min 32 caratteri) su API e
  Worker; user-secrets in sviluppo, variabile `Agent__ApiKey` in produzione. Confronto a tempo costante
  (SHA-256 + `CryptographicOperations.FixedTimeEquals`, `Security/ApiKeyComparer.cs`), 401 senza loggare
  la chiave. `X-Agent-Key` non è tra gli header CORS ammessi: il browser non può chiamare `/api/agent/*`.
- **Regole di stato** (`VedettaVip.Worker/State/DeviceStateTracker.cs`, coperte da test):
  - un echo ICMP per poll, timeout 1,5 s; hostname risolto via DNS a ogni poll (DNS ko = ICMP ko);
  - **Down** dopo N ICMP falliti consecutivi; da Down/Unknown torna **Up** dopo M riusciti;
  - SNMP (GET `sysUpTime`, timeout 3 s) solo se raggiungibile e `SnmpVersion` è V1 o V2c:
    **Partial** dopo K fallimenti consecutivi, ritorno a Up al primo successo.
  - **Soglie N/M/K configurabili dalla UI**: valori generali nella tabella `MonitoringSettings` (pagina
    Impostazioni, default 3/2/2) e valori specifici per device (colonne nullable su `Devices`, sezione
    "Rilevazione" del pannello device; null = generale). L'API manda le soglie **effettive** in
    `AgentTargetDto.Thresholds` e notifica `TargetsChanged` a ogni modifica; il Worker le applica al tracker a
    ogni poll senza azzerare i conteggi in corso. `Polling:DownAfterFailures` ecc. del Worker sono solo il
    ripiego se l'API non le invia. `None` e `V3` (V3 non ancora supportato, warning nel log) non diventano mai Partial;
  - all'avvio tutto è Unknown e l'agente **non invia mai Unknown** (un riavvio non genera eventi finti).
  - Polling ogni 30 s con `Parallel.ForEachAsync`, `Polling:MaxDegreeOfParallelism` (default 32);
    warning se un ciclo supera l'intervallo.
- **Profili SNMP** (v1/v2c, tabella `SnmpCredentials`, community cifrata con Data Protection, mai restituita alla UI):
  profilo effettivo = quello del device → del cliente (`Customer.SnmpCredentialId`) → predefinito
  (`MonitoringSettings.DefaultSnmpCredentialId`) → `Polling:SnmpCommunity` del Worker (ripiego). L'API risolve e manda la
  community in `AgentTargetDto.SnmpCommunity` (solo agli agenti con `X-Agent-Key`; `PrintMembers` la esclude dal
  `ToString`, mai nei log). Cambiare community, profilo del cliente o predefinito notifica `TargetsChanged`; il Worker
  ricalcola lo stato del device e rifà l'inventario delle interfacce. SNMPv3 non ancora supportato.
- **Target aggiornati subito**: l'API invia `TargetsChanged` sull'hub SignalR `/hubs/agent` (protetto da
  `X-Agent-Key` tramite `Security/AgentKeyMiddleware`, perché gli endpoint filter non valgono per gli hub) a ogni
  create/update/delete di un device (`Services/AgentNotifier`). Il Worker (`Services/AgentHubListener`) ricarica
  subito i target, e lo fa anche dopo ogni (ri)connessione. I target nuovi o con indirizzo/versione SNMP cambiati
  vengono interrogati immediatamente con isteresi azzerata: un device raggiungibile è Up in ~30 s (secondo ping
  al ciclo successivo). Mai due poll in parallelo sullo stesso device. Fallback: refresh periodico
  `Agent:TargetsRefreshSeconds` (120 s) se l'hub non è raggiungibile.
- **Traffico dei link** (implementato, live, non ancora storicizzato):
  - `AgentTargetDto.IfIndexes` = interfacce usate come sorgente da almeno un link; cambiare DeviceId/IfIndex di
    un link notifica `TargetsChanged` agli agenti.
  - Il Worker aggiunge al GET SNMP di `sysUpTime` (stesso pacchetto) `ifHCInOctets`, `ifHCOutOctets`,
    `ifHighSpeed`, `ifName` per ogni interfaccia (8 per GET). **Solo v2c**: in v1 niente Counter64, warning nel
    log. Se il GET con le interfacce fallisce si riprova con il solo sysUpTime (un IfIndex errato non rende Partial).
  - bps = delta contatori × 8 / delta **sysUpTime del device** (`State/TrafficCalculator.cs`, coperto da test).
    Campione scartato e base ripartita se sysUpTime non cresce (riavvio, wrap a 497 giorni) o un contatore cala
    (reset); delta < 1 s ignorato. Il primo valore arriva al secondo poll.
  - A fine ciclo `POST /api/agent/traffic` (`AgentTrafficReportDto`), senza buffer né retry (dato live).
    L'API tiene l'ultimo campione per interfaccia in memoria (`Services/TrafficCache`), lo espone su
    `GET /api/traffic` (solo campioni più giovani di `Agent:TrafficStaleSeconds`, 90 s) e lo inoltra ai browser
    con `TrafficUpdated` sull'hub `/hubs/status`.
  - Mappa: tx = From → To. Interfaccia del device del nodo To ⇒ tx = In; altrimenti (nodo From o terzo device)
    tx = Out. Link con sorgente ma senza dati da più di 90 s: grigio con "n/d"; senza sorgente: grigio
    tratteggiato. Il pannello del link mostra ifName e velocità rilevati. Simulazione di traffico e CPU rimossa
    (`[Cpu]` mostra "?" finché non arriva l'integrazione RouterOS).
- **Inventario interfacce** (`Services/InterfaceInventoryService.cs`, solo SNMP v2c): walk GetBulk di `ifName`,
  `ifAlias` (il comment di RouterOS), `ifHighSpeed`, `ifOperStatus` con `SnmpProbe.WalkColumnAsync` (request-id
  positivi, **non** `Messenger.BulkWalk`). Al primo avvistamento, al cambio di indirizzo/SNMP, ogni
  `Polling:InterfaceInventoryMinutes` (15) e su richiesta (`InterfacesRequested` sull'hub agent); dopo un errore
  riprova tra 5 min. `POST /api/agent/interfaces` sostituisce le righe di `DeviceInterfaces` (delete + insert in
  transazione). Nel pannello del link l'interfaccia si sceglie da un combo ("2 — ether1Wan · comment (2.5G)",
  "[down]"); con elenco vuoto e device v2c il pannello chiede un inventario immediato e attende fino a ~15 s;
  senza elenco resta il campo IfIndex numerico.
- **Storico dei ping**: a ogni poll il Worker accoda `DevicePingDto` (esito, RTT se riuscito; DNS non risolto =
  perso) e lo invia a fine ciclo nel campo `Pings` di `AgentTrafficReportDto`, salvato nello stesso COPY del traffico.
- **Storico del traffico**: `POST /api/agent/traffic` salva anche i campioni nuovi in `Metrics` con COPY binario
  (`MetricWriter`, `NpgsqlDataSource` condiviso con EF). Se il DB non risponde si perde solo lo storico di quel
  campione, il dato live arriva comunque. Lettura: `GET /api/metrics/interface` (`MetricQuery`): `MetricResolution`
  sceglie sorgente e bucket per ~500 punti (grezzi se bucket < 5 min ed entro 7 giorni, poi `Metrics5m` entro
  90 giorni, poi `Metrics1h`), con `time_bucket_gapfill` (bucket senza dati = null, buchi visibili nel grafico).
- **Invio**: cambi di stato subito, snapshot completo ogni `Agent:SnapshotIntervalSeconds` (300 s, deve
  coincidere tra API e Worker). Se l'API non risponde: buffer ordinato in memoria (`Agent:BufferCapacity`,
  10.000; pieno = scarta i più vecchi) e backoff esponenziale con jitter 1→60 s; un solo snapshot in coda.
  401 = errore di configurazione (log Error, backoff massimo); altri 4xx = report scartato.
- **Lato API** (`Services/DeviceStatusService.cs`): un report alla volta (`SemaphoreSlim`; con più istanze
  dell'API servirà `pg_advisory_xact_lock`). Risultati applicati in ordine di `Time`; quelli più vecchi
  dell'ultimo applicato (`ObservedAt`) sono scartati. A ogni cambio: `DeviceStatuses` aggiornato, Event
  `StateChange` (Down=Error, Partial=Warning, Up=Info) con l'istante di rilevazione dell'agente, e dopo il
  commit messaggio SignalR `DeviceStateChanged`. Ritrasmettere un report già elaborato non crea eventi doppi.
- **Stato vecchio**: se l'ultimo report di un device è più vecchio di 2 intervalli di snapshot (10 min),
  `GET /api/maps/{id}` lo mostra Unknown, senza Event (agente fermo ≠ device giù).
- **Agenti**: tabella `Agents` con `LastSeen` aggiornato da ogni report (snapshot compresi);
  `GET /api/agents` → `IsOnline` = LastSeen entro 2 intervalli di snapshot. La mappa interroga l'elenco
  ogni 60 s e mostra un banner se un agente è offline o se nessun agente ha mai inviato dati.
- **ICMP su Linux**: .NET usa socket raw (CAP_NET_RAW), socket ICMP non privilegiati se
  `net.ipv4.ping_group_range` lo consente, altrimenti l'eseguibile `ping`.

## Modello dati (EF Core, migrations `InitialCreate`, `AddDeviceStatusAndAgents`, `AddDeviceInterfaces`)

Entità in `src/VedettaVip.Api/Data/Entities/`, configurazioni Fluent in `Data/Configurations/`,
seed in `Data/SeedData.cs`, migrations in `Data/Migrations/`. DTO ed enum condivisi in
`VedettaVip.Shared/Contracts/` (i `MapNode`/`MapLink` di `VedettaVip.Shared.Models` restano view-model
della UI, con stato e traffico live, e non sono persistiti).

Convenzioni: Id `uuid` (generati con `Guid.CreateVersion7()`), tranne `Event.Id` `bigint identity`;
enum salvati come **testo**; tempi `timestamptz` in UTC; nomi tabelle/colonne PascalCase
(in psql vanno tra doppi apici: `select "Name" from "Devices"`).

- **Device**: Id, Name(128), Address(255, IP o hostname), Type (`Router/Switch/AccessPoint/Server/Other`),
  Icon?, SnmpVersion (`None/V1/V2c/V3`), SnmpCredentialId? (**senza FK**: in futuro punterà a
  *profili* di credenziali condivisi tra più device, cifrati con Data Protection), RouterOsApiEnabled,
  ParentDeviceId? (FK → Device, `SET NULL`; niente auto-riferimento né cicli, verificati dall'API), Enabled.
- **Map**: Id, Name(128), ParentMapId? (FK → Map, `RESTRICT`), BackgroundImage?(512), GridSize (> 0).
- **MapNode**: Id, MapId (FK, `CASCADE`), Kind (`Device/Submap/Static`), DeviceId? (FK, `RESTRICT`),
  SubmapId? (FK → Map, `RESTRICT`), X, Y, LabelTemplate(512), Icon?.
  CHECK di coerenza: Device ⇒ solo DeviceId; Submap ⇒ solo SubmapId; Static ⇒ nessuno dei due
  (il testo del nodo statico è il LabelTemplate, es. "Internet"). SubmapId ≠ MapId.
  Indici unici: `(MapId, DeviceId)` (un device una volta per mappa) e `SubmapId` (una sottomappa ha un solo nodo).
- **MapLink**: Id, MapId (FK, `CASCADE`), FromNodeId/ToNodeId (FK, `CASCADE`, diversi tra loro),
  DeviceId? (FK, `RESTRICT`) + IfIndex? (CHECK: IfIndex richiede DeviceId), SpeedBps (> 0). tx = From → To.
- **Metrics** (hypertable TimescaleDB, migration `AddMetrics`, **fuori dal modello EF**): Time, DeviceId, IfIndex?,
  Name(64), Value (double). Nomi in `Services/MetricWriter.cs` (`MetricNames`): `if.in_bps`, `if.out_bps`
  (IfIndex valorizzato), `icmp.rtt_ms` (solo ping riusciti) e `icmp.loss` (0/1: la media è la frazione di perdita,
  anche negli aggregati), con IfIndex NULL. Chunk
  giornalieri, indice (DeviceId, Name, IfIndex, Time DESC), compressione dopo 2 giorni (segmentby DeviceId/Name/IfIndex),
  **retention 7 giorni**. Nessuna FK verso Devices (l'ingest non deve fallire per un device appena eliminato; le righe
  orfane scadono con la retention).
  - `Metrics5m`: continuous aggregate (Bucket, DeviceId, IfIndex, Name, Avg, Max, Min, Samples), refresh ogni 5 min
    sull'ultimo giorno, **retention 90 giorni**.
  - `Metrics1h`: continuous aggregate **su `Metrics5m`** (media pesata su Samples), refresh orario sugli ultimi
    3 giorni, **retention 2 anni**.
  - Entrambi `materialized_only = false` (real-time: includono i bucket non ancora materializzati).
  - La migration è tutta SQL fuori transazione (`suppressTransaction`, obbligatorio per i continuous aggregate) e
    idempotente (`IF NOT EXISTS`, `if_not_exists => TRUE`): se si interrompe a metà si rilancia.
- **Event**: Id, DeviceId? (FK, `RESTRICT`; null per gli eventi degli agenti), Time, Severity (`Info/Warning/Error/Critical`), Type(64),
  Message(2048), Acknowledged. Indici `(DeviceId, Time DESC)` e parziale sui non confermati.
- **DeviceStatus** (tabella `DeviceStatuses`): DeviceId (PK e FK → Device, **`CASCADE`**), State
  (`Unknown/Up/Partial/Down`), Since e ObservedAt (orologio dell'agente), LastReportAt (orologio dell'API,
  decide lo stato vecchio), LastRttMs?, SnmpOk?, AgentId(64).
- **DeviceInterface** (tabella `DeviceInterfaces`, migration `AddDeviceInterfaces`): PK (DeviceId, IfIndex), Name(128)?,
  Alias(256)?, SpeedBps?, OperStatus (`Up/Down/Other`), UpdatedAt (orologio dell'API). FK → Device **`CASCADE`**
  (inventario derivato, come DeviceStatus).
- **Device**: anche DownAfterFailures?, UpAfterSuccesses?, SnmpDegradedAfterFailures? (CHECK 1–100; null = valore
  generale). Migration `AddDetectionThresholds`. Vendor (`Generic/MikroTik`, testo, default `Generic`; migration
  `AddDeviceVendorAndWinBox`, che ha segnato MikroTik i device con `RouterOsApiEnabled`).
- **MonitoringSettings** (riga unica, CHECK Id = 1, creata dalla migration): DownAfterFailures, UpAfterSuccesses,
  SnmpDegradedAfterFailures (1–100), soglie generali di rilevazione; DashboardEventHours (1–168, default 4, migration
  `AddDashboardSettings`); WinBoxWindowsPath?/WinBoxLinuxPath? (512, migration `AddDeviceVendorAndWinBox`).
- **SnmpCredential** (tabella `SnmpCredentials`, migration `AddSnmpCredentials`): Id, Name(128, unico), Description(512)?,
  CommunityProtected(2048). FK **RESTRICT** da `Devices.SnmpCredentialId`, `Customers.SnmpCredentialId` e
  `MonitoringSettings.DefaultSnmpCredentialId` (l'API risponde 409 con gli utilizzi).
- **RouterOsCredential** (tabella `RouterOsCredentials`, migration `AddRouterOsCredentials`): Name(128, unico), Description?,
  Username(64), PasswordProtected, UseTls, Port (CHECK 1–65535), VerifyCertificate. FK RESTRICT da
  `Devices.RouterOsCredentialId`, `Customers.RouterOsCredentialId`, `MonitoringSettings.DefaultRouterOsCredentialId`.
- **Agent** (tabella `Agents`): Id(64, = `Agent:AgentId` del Worker, default nome macchina), FirstSeen, LastSeen,
  OfflineSince?.
- **Notifiche** (migration `AddNotifications`): `Customers` (Name unico), `Contacts`, `Subscriptions` (CHECK di
  coerenza ambito/cliente/mappa; FK CASCADE da contatto, cliente e mappa), `NotificationSettings` (riga unica Id = 1,
  segreti cifrati), `NotificationDeliveries` (FK Event CASCADE, Contact SET NULL), `DataProtectionKeys`.
  `Events` ha in più AgentId?, FromState?, ToState?, NotifyState (`None/Pending/Done/Suppressed`; gli eventi
  esistenti prima della migration sono `None`; `Maintenance` = rimandato per manutenzione), NotifyAfter?, NotifyNote?,
  AlertKey?, LastReminderAt?, ReminderCount. Migration `AddEventResolvedAt`: ResolvedAt?
  (chiusura automatica al ripristino); gli eventi Info esistenti sono stati marcati presi in carico.

Decisioni:
- **Tutte le FK verso Device sono RESTRICT** (tranne ParentDeviceId, `SET NULL`, e DeviceStatus, `CASCADE`:
  **eccezione motivata**, è stato derivato e non storico; con RESTRICT nessun device già interrogato sarebbe
  più cancellabile; lo storico resta negli Event, che sono RESTRICT). `DELETE /api/devices/{id}`
  risponde **409** con `DeviceInUseDto` (elenco mappe in cui compare come nodo o come sorgente di
  traffico di un link, numero di eventi). Con SET NULL su MapLink.DeviceId il CHECK su IfIndex avrebbe
  fatto fallire la delete.
- **Invariante delle sottomappe**: per ogni nodo Submap, `Maps[SubmapId].ParentMapId == MapId del nodo`.
  L'API la mantiene nello **stesso SaveChanges** (stessa transazione) quando crea, sposta o elimina un nodo
  Submap: creazione/spostamento impostano ParentMapId (con controllo anti-ciclo risalendo gli antenati),
  l'eliminazione lo azzera (la sottomappa diventa radice). Chi crea nodi Submap fuori dall'API deve rispettarla.
- Spostare un nodo su un'altra mappa elimina i suoi link (appartengono alla mappa di origine).
- Lo stato dei nodi viene da DeviceStatus (vedi "Agente di polling ↔ API"). **Nodi Submap: stato aggregato**
  (`VedettaVip.Shared/Models/SubmapStatus`, coperto da test): il peggiore tra i dispositivi della sottomappa e di tutte le
  discendenti (Down > Partial > Up; Unknown solo se nessuno è noto; i device in manutenzione non colorano ma sono contati).
  `GET /api/maps/{id}` manda `MapNodeDto.SubmapMembers` (device + stato + manutenzione); il client ricalcola a ogni
  `DeviceStateChanged` senza ricaricare. Sulla mappa: badge con il numero di Down+Partial, tooltip con i conteggi,
  variabili d'etichetta `[Summary]` `[Down]` `[Partial]` `[Up]` `[Total]` (default dei nodi Submap `[Name]\n[Summary]`,
  migration `SubmapSummaryLabel` per le etichette di default esistenti).
- Le violazioni di vincolo PostgreSQL (race tra richieste) diventano 409/400 (`Endpoints/DbProblems.cs`).
- Nessun `HasDefaultValue` su bool/int: EF sostituirebbe il valore CLR di default (false/0) con quello del DB.
- Le migration **non** vengono applicate all'avvio: si usa `dotnet ef database update` (o lo script SQL).

Le credenziali (community SNMP, utenti RouterOS) **non** vanno mai salvate in chiaro:
usare ASP.NET Core Data Protection o un secret store.

## API (VedettaVip.Api, minimal API in `src/VedettaVip.Api/Endpoints/`)

| Metodo | Route | Note |
|---|---|---|
| GET | `/api/maps` | `MapSummaryDto[]` (Id, Name, ParentMapId, NodeCount) |
| GET | `/api/maps/{id}` | `MapDto` con nodi (Name/Address/DeviceType dal device o dalla sottomappa) e link |
| POST | `/api/maps` | `MapUpsertDto` (Name, BackgroundImage?, GridSize) → 201 `MapDto`; nasce mappa radice |
| PUT | `/api/maps/{id}` | `MapUpsertDto` → 204; `ParentMapId` non modificabile (lo gestiscono i nodi Submap) |
| DELETE | `/api/maps/{id}` | 204 con nodi e link (CASCADE); **409** se rappresentata da un nodo Submap; le sue sottomappe diventano radici |
| GET | `/api/maps/{mapId}/nodes/{nodeId}` | `MapNodeDto` |
| POST | `/api/maps/{mapId}/nodes` | `CreateMapNodeDto` → 201; 400 se incoerente, 409 se duplicato o ciclo |
| PUT | `/api/maps/{mapId}/nodes/{nodeId}` | `UpdateMapNodeDto` (LabelTemplate, Icon) → 200 `MapNodeDto`; vuoto = etichetta di default |
| PUT | `/api/maps/{mapId}/nodes/{nodeId}/position` | `{ "x", "y" }` → 204 / 404 |
| POST | `/api/maps/{mapId}/nodes/{nodeId}/move` | `MoveMapNodeDto` → 200; elimina i link del nodo |
| DELETE | `/api/maps/{mapId}/nodes/{nodeId}` | 204; i link cadono in CASCADE |
| GET/PUT/DELETE | `/api/maps/{mapId}/links/{linkId}` | `MapLinkDto` / `MapLinkUpsertDto` → 204 / 204 |
| POST | `/api/maps/{mapId}/links` | `MapLinkUpsertDto` → 201; nodi diversi e della stessa mappa, IfIndex solo con DeviceId |
| GET/POST | `/api/devices` | `DeviceDto[]` / `DeviceUpsertDto` → 201; `?mapId=` aggiunge anche il nodo su quella mappa (sotto i nodi esistenti, stesso SaveChanges) |
| GET/PUT/DELETE | `/api/devices/{id}` | DELETE → 409 `DeviceInUseDto` se su una mappa o con eventi; `?purgeEvents=true` cancella anche gli eventi (le mappe bloccano sempre) |
| POST | `/api/devices/import` | `DeviceImportRequestDto` (CSV, DryRun, duplicati Skip/Update, mappa, crea clienti solo Admin) → `DeviceImportResultDto` riga per riga; Operatore |
| GET | `/api/devices/export` | CSV di tutti i device (stesse colonne dell'import, `;`, BOM UTF-8) |
| GET | `/api/devices/{id}/interfaces` | `DeviceInterfacesDto` (UpdatedAt + interfacce dall'ultimo inventario) |
| POST | `/api/devices/{id}/interfaces/refresh` | 202; chiede agli agenti un inventario immediato (`InterfacesRequested`) |
| POST | `/api/agent/interfaces` | **X-Agent-Key**; `AgentInterfacesReportDto` → 204 (404 se il device non esiste più) |
| GET | `/api/devices/overview` | `DeviceOverviewDto[]`: device + stato (Unknown se disabilitato o report vecchio), RTT, SNMP ok, agente, mappe |
| GET | `/api/agent/targets` | **X-Agent-Key**; `AgentTargetDto[]` dei device abilitati, con interfacce da misurare e soglie effettive |
| GET/PUT | `/api/settings/notifications` | `NotificationSettingsDto` / `NotificationSettingsUpdateDto` (segreti write-only) |
| POST | `/api/settings/notifications/test-email`, `/test-telegram` | prova con le impostazioni salvate; 502 con l'errore del server |
| GET/POST, PUT/DELETE | `/api/customers`, `/api/customers/{id}` | clienti (DELETE: device senza cliente, iscrizioni eliminate) |
| GET/POST, PUT/DELETE | `/api/contacts`, `/api/contacts/{id}` | contatti con iscrizioni (sostituite per intero nel PUT) |
| GET | `/api/events` | `?unacknowledgedOnly&since&before&limit&deviceId&customerId&noCustomer&severity` (max 1000; `noCustomer` = device senza cliente ed eventi degli agenti) → `EventDto[]` con esito delle notifiche |
| GET/POST, PUT/DELETE | `/api/snmp-credentials`, `/api/snmp-credentials/{id}` | profili SNMP (`SnmpCredentialDto` senza community; upsert con community write-only, vuota = invariata); DELETE 409 se in uso; scritture Admin |
| PUT | `/api/snmp-credentials/default` | `SnmpDefaultDto` (profilo predefinito, null = community del Worker) |
| GET/PUT | `/api/settings/dashboard` | `DashboardSettingsDto` (ore di eventi recenti nella Home, 1–168, default 4); PUT Admin |
| PUT | `/api/events/{id}/ack` | `EventAckDto` (presa in carico / riapertura) |
| GET | `/api/events/{id}/deliveries` | `DeliveryDto[]` (canale, contatto, destinazione, esito, tentativi, errore) |
| GET/PUT | `/api/settings/metric-thresholds` | `MetricThresholdsDto` (latenza, perdita, utilizzo link, finestra); PUT Admin |
| GET/POST, PUT/DELETE | `/api/maintenance`, `/api/maintenance/{id}` | finestre di manutenzione (con ActiveNow, NextStart); scritture Operatore |
| GET/PUT | `/api/settings/detection` | `DetectionThresholdsDto` (soglie generali); il PUT notifica gli agenti |
| POST | `/api/agent/status` | **X-Agent-Key**; `AgentStatusReportDto` → `AgentStatusAckDto` |
| GET | `/api/agents` | `AgentDto[]` (AgentId, LastSeen, IsOnline) |
| POST | `/api/auth/login`, `/login-2fa`, `/login-recovery` | password → `LoginResultDto` (RequiresTwoFactor); codice dell'app o di recupero |
| GET/POST | `/api/auth/2fa`, `/2fa/setup`, `/enable`, `/recovery-codes`, `/disable`, `/forget-browser` | verifica in due passaggi dell'utente corrente |
| POST | `/api/users/{id}/reset-2fa` | azzera la verifica in due passaggi di un altro utente; Admin |
| WS | `/hubs/status` | SignalR, server → client `DeviceStateChanged` (`DeviceStateChangedDto`), `TrafficUpdated` (`InterfaceTrafficDto[]`), `RouterOsUpdated`, `MapsChanged` (`MapsChangedDto`) |
| POST | `/api/agent/traffic` | **X-Agent-Key**; `AgentTrafficReportDto` → 204 |
| GET | `/api/metrics/interface` | `?deviceId&ifIndex&from&to` (ISO 8601, default ultime 6 h, max 731 giorni) → `InterfaceSeriesDto` (Source, BucketSeconds, punti avg/max in/out) |
| GET | `/api/metrics/device` | `?deviceId&from&to` → `DeviceLatencySeriesDto` (RTT medio/max, perdita %) |
| GET | `/api/metrics/device/interfaces` | `?deviceId` → interfacce con storico del traffico negli ultimi 90 giorni |
| GET/POST, PUT/DELETE | `/api/routeros-credentials`, `/api/routeros-credentials/{id}` | profili RouterOS (password write-only); DELETE 409 se in uso; scritture Admin |
| PUT | `/api/routeros-credentials/default` | `RouterOsDefaultDto` |
| POST | `/api/agent/routeros` | **X-Agent-Key**; `AgentRouterOsReportDto` → 204 |
| GET | `/api/routeros` | `RouterOsSampleDto[]` recenti (ultima lettura per device, senza interfacce e peer, con `WatchProblems`) |
| GET | `/api/devices/{id}/routeros` | `DeviceRouterOsDetailDto`: ultima lettura completa (interfacce, peer WireGuard) ed elementi sorvegliati |
| PUT | `/api/devices/{id}/routeros/watches` | `RouterOsWatchToggleDto` → elenco aggiornato; Operatore |
| GET | `/api/metrics/device/routeros` | `?deviceId&from&to` → `DeviceRouterOsSeriesDto` (CPU media/picco, memoria, temperatura) |
| POST | `/api/agent/neighbors` | **X-Agent-Key**; `AgentNeighborsReportDto` → 204 (sostituisce i vicini del device) |
| GET | `/api/discovery` | `DiscoveryResultDto`: link mancanti e dispositivi nuovi proposti |
| GET/POST, DELETE | `/api/discovery/scans`, `/api/discovery/scans/{id}` | scansioni di subnet (ultime 20 con avanzamento); Operatore per avviare/eliminare |
| GET/POST | `/api/agent/scans/{id}` | **X-Agent-Key** + `X-Agent-Id`: presa del compito (409 se di un altro agente) e avanzamento/esito |
| POST | `/api/discovery/accept`, `/ignore`, `/refresh` | conferma (`DiscoveryAcceptDto`), ignora/ripristina, "Scopri ora"; Operatore |
| GET/PUT | `/api/settings/winbox` | `WinBoxSettingsDto` (percorsi di WinBox 4 per Windows e Linux, null = ricerca); GET per tutti, PUT Admin |
| GET | `/api/winbox/handler/{windows\|linux}` | installer del gestore `winbox://` con il percorso già scritto (`.cmd` / `.sh`) |
| GET | `/api/traffic` | `InterfaceTrafficDto[]` recenti (ultimo campione per DeviceId + IfIndex) |
| WS | `/hubs/agent` | SignalR, **X-Agent-Key**; server → agente `TargetsChanged` (nessun argomento), `InterfacesRequested` (Guid) |

- JSON: enum come stringhe (gli interi sono rifiutati), errori come ProblemDetails, validazione
  DataAnnotations integrata di .NET 10 (`AddValidation`).
- CORS: policy `VedettaVipWeb`, origini in `Cors:AllowedOrigins` (in sviluppo quelle di VedettaVip.Web:
  `https://localhost:7266`, `http://localhost:5032`), con `AllowCredentials` e header `X-SignalR-*`
  richiesti dal client SignalR.
- **Autenticazione utenti** (ASP.NET Core Identity, account locali, tabelle `AspNet*` nello stesso DB):
  - cookie `VedettaVip.Auth` emesso dall'API: HttpOnly, SameSite=Lax, Secure in produzione (`Auth:SecureCookies`,
    default true fuori da Development), scadenza 8 h scorrevole ("Ricordami" = cookie persistente). Chiavi Data
    Protection nel DB: le sessioni sopravvivono ai riavvii. API senza redirect: 401/403.
  - **fallback policy = utente autenticato** su tutto; `AllowAnonymous` solo su `/api/auth/me|login|login-2fa|login-recovery|logout|setup`,
    `/api/agent/*` (chiave `X-Agent-Key`), hub agent, OpenAPI in sviluppo.
  - **Ruoli** (uno per utente, creati dalla migration `AddIdentity`): `Viewer` (lettura), `Operator` (+ scritture
    su mappe, nodi, link, device, refresh interfacce, presa in carico: `.RequireOperator()`), `Admin` (+ clienti,
    contatti, impostazioni, utenti: `.RequireAdmin()`). `GET /api/customers` e `/api/settings/detection` sono per
    tutti (servono al pannello del device).
  - Password: almeno 10 caratteri (nessuna regola di complessità); blocco dopo 5 tentativi per 15 min (si sblocca
    reimpostando la password). Utente disattivato o ruolo cambiato: security stamp aggiornato, sessioni
    rivalidate entro **1 minuto** (`SecurityStampValidatorOptions.ValidationInterval`).
  - **Primo amministratore**: senza utenti l'API scrive nel log "Codice di setup: XXXX-XXXX-XXXX" (`SetupCode`,
    nuovo a ogni avvio); la pagina Accedi lo chiede per creare l'admin. Mai due setup (409 se esistono utenti).
  - **CSRF**: le scritture su `/api/*` (escluso `/api/agent`) richiedono l'header `X-VedettaVip-Request`
    (`CsrfHeaderMiddleware`), ammesso dal CORS solo per le origini di VedettaVip.
  - **Verifica in due passaggi** (facoltativa, per utente; `Endpoints/TwoFactorEndpoints`, coperta da test anche end-to-end):
    TOTP RFC 6238 (SHA-1, 6 cifre, 30 s, ±1 passo) con il provider Authenticator di Identity; qualsiasi app (Google/Microsoft
    Authenticator, Aegis, 2FAS, Bitwarden…). Niente SMS né codici via email/Telegram.
    - Attivazione da `/account`: `POST /api/auth/2fa/setup` (chiave nuova, URI `otpauth://`, QR come data URI SVG con
      Net.Codecrete.QrCodeGenerator), `enable` con il primo codice → 10 codici di recupero `XXXXX-XXXXX` mostrati una volta
      (copia/scarica .txt); `recovery-codes` (col codice dell'app) li rigenera; `disable` con la password; `forget-browser`.
    - Login: `POST /api/auth/login` → `LoginResultDto.RequiresTwoFactor`; Identity emette solo il cookie temporaneo
      `VedettaVip.TwoFactor` (5 min, nessun accesso all'API) e il client chiede il codice: `login-2fa` (con "non chiedere su
      questo browser per 7 giorni", cookie `VedettaVip.TwoFactorRemember`, `TwoFactorDefaults.RememberBrowserDays`) oppure
      `login-recovery` (monouso, maiuscole/spazi normalizzati). I codici errati contano per il blocco (5 tentativi).
    - **Chiave e codici cifrati** con Data Protection da `Security/ProtectedUserStore` (override di Get/SetTokenAsync per il
      provider interno `[AspNetUserStore]`; Identity li salverebbe in chiaro); valore non decifrabile = assente.
    - Le operazioni che cambiano il security stamp (setup, enable, disable) chiamano `RefreshSignInAsync`: la sessione
      corrente resta, le altre dell'utente e i browser ricordati cadono entro 1 minuto.
    - Promemoria: `Components/TwoFactorReminder` (isola WASM nel `MainLayout`) mostra a chi non l'ha attiva un avviso con le
      istruzioni (`CurrentUserDto.TwoFactorEnabled`), nascosto su `/account` e `/login`; "Più tardi" = sessionStorage,
      "Non mostrare più" = localStorage (`vedettavip.tfaReminder.<userId>`); sparisce all'attivazione (`CurrentUser.Changed`).
      Nel pannello "Nuovo utente" una nota spiega che la attiva l'utente (la chiave deve stare sul suo telefono).
    - Admin: colonna 2FA in `/settings/users` e `POST /api/users/{id}/reset-2fa` (telefono perso; non su sé stessi), che
      cancella chiave e codici e chiude le sessioni dell'utente.
  - Non si può disattivare/eliminare sé stessi né togliere l'ultimo admin attivo. La presa in carico registra
    `AcknowledgedBy` (nome visualizzato) e `AcknowledgedAt`.
  - Client: `ApiCredentialsHandler` (credentials: include, header CSRF, su 401 → `/login?returnUrl=`),
    `IncludeCredentialsHandler` per la negotiate SignalR, `CurrentUser` (ruolo, per mostrare/nascondere i comandi),
    `AdminOnly` (sezioni riservate). Pagine `/login` (con setup e secondo passo 2FA), `/account` (cambio password, verifica in due passaggi), `/settings/users` (admin),
    `UserMenu` in alto. La UI nasconde soltanto: i permessi li applica l'API.
  - API e Web restano in ascolto solo su localhost in sviluppo; in produzione dietro reverse proxy HTTPS.
- Connection string `ConnectionStrings:VedettaVip`, **mai in appsettings**: in sviluppo negli user-secrets
  di VedettaVip.Api, in produzione nella variabile `ConnectionStrings__VedettaVip`. Se manca, l'API non parte.
- Client nel frontend: `VedettaVip.Web.Client/Services/VedettaVipApiClient.cs`, chiamate dirette dal browser.
  URL in `Api:BaseUrl` di `VedettaVip.Web.Client/wwwroot/appsettings.{Environment}.json` (in sviluppo
  `http://localhost:5197`); se assente si usa la stessa origine del sito (reverse proxy in produzione).
  Nota: `wwwroot/appsettings*.json` è pubblico, non metterci segreti. La pagina `/map` gira in
  InteractiveWebAssembly **senza prerender** (il server non deve chiamare l'API al posto del browser).
- Esempi pronti in `src/VedettaVip.Api/VedettaVip.Api.http`. Seed: mappa "Sede principale"
  (`0198f000-0000-7000-8000-000000000001`) con 4 device, sottomappa "Filiale Nord", nodo statico "Internet".

### Integrazione RouterOS (implementata: risorse e salute)

- **Protocollo**: API classica di RouterOS (v6 e v7), client scritto in casa `VedettaVip.Worker/Probes/RouterOsApiClient`
  (parole a lunghezza variabile, login v6.43+/v7 e login a challenge MD5 dei v6 precedenti, TLS con `SslStream` per
  api-ssl, timeout via CancellationToken). Una connessione per lettura. Coperto da test con un finto router in-process.
- **Profili** `RouterOsCredentials` (Impostazioni → Profili RouterOS, Admin): utente, password cifrata con Data
  Protection (mai restituita), servizio api/api-ssl, **porta configurabile**, verifica del certificato (di solito spenta:
  certificati autofirmati). Risoluzione come per SNMP: device → cliente → predefinito; nessun profilo = device non letto.
  Solo i device con `RouterOsApiEnabled`. `AgentTargetDto.RouterOs` (password esclusa da `PrintMembers`).
- **api-ssl** richiede un certificato assegnato al servizio sul router: senza, RouterOS offre solo cifrari anonimi che
  .NET non negozia. I comandi per l'utente read-only (`policy=read,api`) sono nella card dei profili.
- **Worker** `RouterOsPollingService`: ogni `Polling:RouterOsIntervalSeconds` (60) legge `/system/resource` e
  `/system/health` (`Probes/RouterOsReader`, formati v6 ad attributi e v7 name/value, uptime "2w3d4h" e "1d02:03:04"),
  `Polling:RouterOsTimeoutMs` (8000) per router, salta i device Down. Errori (login, timeout, TLS) → `Ok = false` con il
  motivo, loggati solo quando cambiano; non cambiano lo stato del device. `Probes/RouterOsErrors` traduce le eccezioni
  nella causa probabile (porta chiusa, IP non ammesso in /ip service, api-ssl senza certificato, certificato non
  attendibile, servizio sbagliato per la porta; coperto da test con socket veri). Ogni lettura riporta il servizio
  usato (`RouterOsSampleDto.Service`, es. "api-ssl :8729"), mostrato nei tooltip.
- **api-ssl sul router**: certificato firmato da una CA locale (su alcune versioni il self-sign diretto dà "CA not found"):
  `/certificate add name=vedettavip-ca ... key-usage=key-cert-sign,crl-sign`, `sign vedettavip-ca`, poi il certificato del
  servizio `sign vedettavip-api ca=vedettavip-ca` e `/ip service set api-ssl certificate=vedettavip-api`.
- **API**: `POST /api/agent/routeros` → `RouterOsCache` (ultima lettura, `Agent:RouterOsStaleSeconds` 180), SignalR
  `RouterOsUpdated`, storico in `Metrics` (`ros.cpu_pct`, `ros.mem_pct`, `ros.temp_c`, `ros.voltage_v`).
- **UI**: variabili d'etichetta `[Cpu]` `[Mem]` `[Temp]` `[Volt]` `[Uptime]` `[Version]` `[Board]` aggiornate dal vivo;
  colonna RouterOS nella pagina Dispositivi ("CPU 12% · RAM 25% · 52 °C", tooltip con versione/modello/uptime, "API ko"
  con il motivo); grafico `DeviceRouterOsChart` (CPU media e picco, memoria, temperatura; bucket minimo 60 s).
- **Soglie CPU e temperatura** (seconda tappa): generali in Impostazioni → Monitoraggio (`MonitoringSettings.RouterOsCpuThresholdPct`
  90 %, `RouterOsTemperatureThresholdC` 75 °C) e per device (`Device.CpuThresholdPct`, `TemperatureThresholdC`; 0 =
  disattivata), valutate da `ThresholdMonitor` sulla media della finestra (minimo metà dei campioni attesi). Chiusura sotto
  l'80% per la CPU, **5 °C sotto** per la temperatura (`ThresholdRules.ClearLevel`). AlertKey `ros.cpu` / `ros.temp`.
- **Interfacce e tunnel WireGuard sorvegliati** (`RouterOsWatches`, migration `RouterOsThresholdsAndWatches`): il Worker
  legge `/interface` e `/interface/wireguard/peers` (v6: nessun WireGuard); nella scheda RouterOS del device si spunta cosa
  sorvegliare (salvato subito, `PUT /api/devices/{id}/routeros/watches`). `RouterOsWatchRules` (coperte da test):
  interfaccia giù se non running, disabilitata o sparita; peer WireGuard giù se disabilitato, senza handshake o con
  l'ultimo handshake oltre **180 s** (l'interfaccia WireGuard resta running anche a tunnel fermo; serve persistent-keepalive).
  Avviso dopo **2 letture giù di fila**, chiusura alla prima su. `RouterOsWatchService` (a ogni report) usa eventi
  ThresholdRaised/Cleared con AlertKey `if:<nome>` / `wg:<chiave pubblica>`: ritardo di invio, flap, manutenzione,
  promemoria, presa in carico e chiusura automatica sono quelli delle soglie; oggetto delle notifiche "INTERFACCIA GIÙ" /
  "TUNNEL GIÙ" e "… RIPRISTINATA/O" (`AlertContext.AlertKey`). Togliere la sorveglianza chiude l'avviso senza notifica.
- `GET /api/routeros` e SignalR mandano le letture **senza** interfacce e peer (pesanti), con `WatchProblems` (badge "N giù"
  nella colonna RouterOS); la lettura completa è in `GET /api/devices/{id}/routeros`.
- Da fare: IPsec, PPP, EoIP/GRE con regole specifiche, MNDP.

### Apri con WinBox (implementato)

- **Produttore** `Device.Vendor` (`DeviceVendor`: Generic, MikroTik): select "Produttore" nel pannello del device; abilitare
  l'API RouterOS lo porta a MikroTik (UI e API, `DeviceEndpoints.VendorOf`); colonna CSV `produttore` (mikrotik/generic);
  la Scoperta propone MikroTik se la proposta è RouterOS o il MAC è MikroTik (`DiscoveryPlanner.VendorOf`).
  `MapNodeDto.Vendor` e `DeviceDto.Vendor` lo portano al client.
- **Link** `winbox://<indirizzo>` (`Services/WinBoxLink`), **senza credenziali**: WinBox chiede utente e password o usa i
  suoi indirizzi salvati. Nessuna password di accesso ai router è salvata in VedettaVip (scelta voluta).
- **Menu contestuale del nodo** (`Components/NodeMenu`, clic destro su un nodo, anche in sola visualizzazione): Apri con
  WinBox (solo MikroTik), Copia indirizzo, Grafici / Apri la sottomappa, Proprietà del nodo (in Modifica), link alla pagina
  del gestore. Pagina Dispositivi: bottone **WinBox** sulle righe MikroTik.
- **Gestore sul PC** (un browser non avvia programmi): installer generati da `Services/WinBoxHandlerScripts` dai modelli in
  `VedettaVip.Api/Resources/WinBox/` (risorse incorporate), con il percorso delle Impostazioni al posto di `__WINBOX_PATH__`
  (quotato per PowerShell, apici tipografici compresi, o per sh). Per l'utente corrente, senza admin/sudo:
  - Windows: un solo `.cmd` (prima riga `<# :` = etichetta batch + commento PowerShell: la parte batch esegue il resto
    con PowerShell). Cerca WinBox (percorso impostato con variabili d'ambiente, posizioni comuni, poi finestra "Apri file"),
    scrive `%LOCALAPPDATA%\VedettaVip\winbox-handler.ps1` e `HKCU\Software\Classes\winbox`. `/uninstall` toglie tutto.
  - Linux: `sh vedettavip-winbox-linux.sh [percorso]` → `~/.local/bin/vedettavip-winbox`, `.desktop` con
    `x-scheme-handler/winbox`, `xdg-mime default`. `--uninstall`.
  - Il gestore accetta solo IP, hostname o MAC (`^[A-Za-z0-9:][A-Za-z0-9.:-]{0,252}$`): qualsiasi sito può aprire un link
    `winbox://`. Coperto da test (anche l'installer Linux eseguito davvero con un finto WinBox).
- **Impostazioni → WinBox** (Admin): percorsi per Windows e Linux + download. Pagina **`/winbox`** ("Gestore WinBox", per
  tutti gli utenti, raggiungibile dal menu del nodo): download e istruzioni. macOS non ancora previsto.

### Discovery (prima tappa: vicini e link)

- **Raccolta** (`VedettaVip.Worker/Services/NeighborDiscoveryService`): ogni `Polling:NeighborDiscoveryMinutes` (30), al primo
  avvistamento e su "Scopri ora" (`NeighborsRequested` sull'hub agent, `POST /api/discovery/refresh`). Device Down saltati.
  - MikroTik con accesso API: `/ip/neighbor` (MNDP, LLDP e CDP già uniti; `interface` "ether2,bridge" → porta).
  - Altri SNMP v2c: LLDP-MIB (lldpRemTable, lldpRemManAddrTable per l'IP nell'indice, lldpLocPortTable) e CISCO-CDP-MIB
    (`SnmpProbe.WalkSubtreeAsync` conserva l'indice completo). Parser puri in `Probes/NeighborParsers`, coperti da test.
  - `POST /api/agent/neighbors` sostituisce i vicini del device (`DeviceNeighbors`, CASCADE).
- **Proposte** (`Services/DiscoveryPlanner`, puro, coperto da test anche con letture reali): vicino = device noto se ha
  lo stesso indirizzo, o lo stesso nome se univoco.
  - Noto → **link mancante** se i due non sono già collegati su nessuna mappa; le letture dei due lati dello stesso cavo
    si uniscono (uplink ridondanti restano distinti); mappa in comune (altrimenti avviso); sorgente di traffico =
    interfaccia nell'inventario SNMP (`FindInterface`: anche "bridge/ether2" → "ether2", come RouterOS in LLDP-MIB).
  - Sconosciuto → **dispositivo nuovo** (chiave MAC, poi IP, poi nome), nome = identity o modello, tipo dedotto
    (`GuessType`: CRS/CSS switch, cAP/wAP AP, "Switch" nel modello prima della capacità router, telefoni, Cisco WS-C),
    cliente/mappa/padre da chi l'ha visto; senza IPv4 annunciato l'indirizzo va inserito a mano.
- **Pagina `/discovery`** (voce "Scoperta"): link e dispositivi proposti, modificabili, "Aggiungi selezionati"
  (`POST /api/discovery/accept`: ricalcola le proposte e crea device, nodo sotto i nodi esistenti e link verso chi l'ha
  visto, in un SaveChanges), "Ignora" (`DiscoveryIgnores`, ripristinabili). Niente viene aggiunto senza conferma.
- **Tabella ARP** (seconda tappa): stesso ciclo dei vicini. MikroTik via API `/ip/arp` unita ai lease
  `/ip/dhcp-server/lease` (hostname e commento danno il nome); altri **router/firewall** (`AgentTargetDto.IsRouter`) via
  SNMP `ipNetToMediaTable`. Salvata in `DeviceArpEntries` (sostituita a ogni lettura, se letta).
- **Scansione di subnet** (`SubnetScanService` nel Worker): dalla pagina (`POST /api/discovery/scans`, IPv4 da /22 a /32,
  cliente e mappa facoltativi) → `ScanRequested` sull'hub → il primo agente la prende (`GET /api/agent/scans/{id}`, header
  `X-Agent-Id`, gli altri 409) con le community dei profili SNMP. Ping (64 in parallelo, 1 s), poi sugli host vivi DNS
  inverso, SNMP v2c `sysName`/`sysDescr` (prima community che risponde) e porte TCP 22, 23, 80, 443, 554, 3389, 8728,
  8729, 9100 (solo connessione; Winbox escluso). Avanzamento ogni ~10 s, esito in `DiscoveryScanHosts`; conta l'ultima
  scansione completata di ogni rete.
- **Unione delle origini**: la chiave delle proposte è l'**IP** (poi MAC, poi nome): vicini, ARP e scansioni dello stesso
  host diventano una proposta; un vicino LLDP con solo il MAC prende l'IP dall'ARP. Nome: identity → sysName → commento
  DHCP → hostname DHCP → DNS (accorciato) → modello da piattaforma o sysDescr. Tipo: vicini, poi `GuessHostType`
  (sysDescr, porte: 9100 stampante, 554 telecamera, 3389 PC, 8728/8729 MikroTik; nomi). Con risposta SNMP il dispositivo
  nasce con SNMP v2c e il profilo trovato. I link si creano solo verso i vicini, non verso chi l'ha visto in ARP.
- Pagina: riquadro "Scansiona una rete" con avanzamento, filtro per origine (vicini/ARP/scansione), badge d'origine,
  "seleziona i visibili", massimo 200 righe (poi "mostra tutti"), "Ignora selezionati" (link e dispositivi selezionati,
  a blocchi di 1000 per richiesta; "ripristina N ignorati" li riporta tutti).
  Filtri anche per "visto da" (router o scansione) e cliente (quello scelto nella riga). "Elimina selezionati"
  (`POST /api/discovery/forget`) cancella le letture salvate delle proposte (righe ARP e vicini dei router che le hanno
  viste, host della scansione): senza memoria, l'host ricompare alla prossima lettura/scansione se c'è ancora.
- **Sedi con la stessa rete** (la stessa rete privata in più sedi): stesso IP con MAC diversi = host diversi, una proposta per MAC
  (chiave `dev:IP|mac`); chi non ha MAC (scansione) va con l'unico gruppo visto da router dello stesso cliente, altrimenti
  resta a parte (`dev:IP|-`). Un "Ignora" sulla vecchia chiave `dev:IP` vale per tutti. Un device esistente con lo stesso
  IP nasconde l'host (e fa da vicino noto) solo se è dello stesso cliente del router (o uno dei due non ha cliente).
- **Produttore dal MAC (OUI)**: `third-party/ieee-oui/generate.py` scarica MA-L/MA-M/MA-S da standards-oui.ieee.org (o
  `--from <cartella>`) e scrive `VedettaVip.Api/Resources/oui.tsv.gz` (risorsa incorporata, ~54k prefissi; rigenerare ogni
  tanto, `VERSION`). `Services/MacVendors.Lookup`: prefisso più lungo (9, 7, 6 cifre hex); bit "locally administered"
  (0x02 nel primo byte) = "MAC privato (casuale)" (telefoni, Windows/Android con MAC casuale); nome accorciato
  (marchi noti, via suffissi societari). Nella proposta: campo `Vendor`, nome di riserva "<produttore> <IP>" al posto del
  solo IP, tipo da `DiscoveryPlanner.VendorType` solo se le altre fonti dicono "Altro" (marchi monotipo: Yealink → telefono,
  Kyocera → stampante, VMware/Proxmox → server...; schede madri PC solo con RDP aperto).
- Da fare: auto-layout dei nodi nuovi, link fra mappe diverse.

### Notifiche (implementate: email e Telegram)

- **Destinatari**: `Customers` (clienti; `Device.CustomerId`, FK `SET NULL`), `Contacts` (email e/o chat Telegram, almeno
  un canale) e `Subscriptions` del contatto: ambito `All` / `Customer` / `Map` (la mappa include le sottomappe),
  `Filter` (`DownOnly` / `DownAndPartial`), `NotifyRecovery`. Destinatari di un evento sul device D: iscritti a
  All + al cliente di D + a ogni mappa (con gli antenati) che contiene D; un solo invio per contatto e canale.
  Eventi degli agenti: solo iscritti a All.
- **Outbox = Event**: `DeviceStatusService` crea l'evento con `NotifyState = Pending` e `NotifyAfter = adesso +
  ritardo` (`NotificationSettings.DelaySeconds`, default 60) se notificabile (`AlertRules.KindOf`: Down, Partial,
  ripristino da Down/Partial; Unknown → Up no). `Services/NotificationDispatcher` (ogni 5 s) prepara gli eventi
  scaduti e salva l'esito **evento per evento** (gli eventi successivi dello stesso giro lo leggono):
  - soppresso se Down e un antenato (`ParentDeviceId`) è Down ("dipende da X");
  - soppresso se il problema è rientrato entro il ritardo (flap), e allora anche il suo ripristino;
  - altrimenti una `NotificationDelivery` per contatto e canale attivo, con nome/destinazione copiati.
  Invio con ritentativi (`AlertRules.RetryDelay` 1, 2, 5, 10, 30, 60 min, poi 2 h; `MaxAttempts` 8 → Failed),
  salvataggio dopo ogni invio. Testo in `AlertMessage` (orari nel fuso di `TimeZoneId`, cliente, mappe, per i Down
  l'elenco dei dipendenti giù, link a `PublicUrl`). Un'istanza sola dell'API (più istanze: FOR UPDATE SKIP LOCKED).
- **Agenti**: `Services/AgentMonitor` (ogni 30 s) marca offline l'agente senza report da 2 intervalli di snapshot
  (`Agent.OfflineSince`) ed emette `AgentOffline`; il primo report successivo emette `AgentOnline`.
  `Event.DeviceId` è nullable, con `AgentId` (CHECK: almeno uno dei due).
- **Canali**: email con **MailKit** (`EmailSender`): sicurezza `None` / `StartTls` / `SslOnConnect` (465) / `Auto`,
  autenticazione se c'è l'utente (meccanismo negoziato), verifica certificato disattivabile. Telegram Bot API
  (`TelegramSender`): l'HttpClient è registrato con `RemoveAllLoggers()` perché il token è nell'URL.
- **Segreti**: password SMTP e token Telegram cifrati con **ASP.NET Core Data Protection** (`SecretProtector`), chiavi
  nella tabella `DataProtectionKeys` (`PersistKeysToDbContext`). La UI non li rilegge mai (`HasSmtpPassword`,
  `HasTelegramToken`); nel PUT: null = invariato, "" = rimuovi. In produzione valutare `ProtectKeysWithCertificate`.
- **Soglie sulle metriche** (`Services/ThresholdMonitor`, ogni minuto, regole pure in `ThresholdRules`, coperte da test):
  latenza (RTT medio dei ping riusciti) e perdita (% di ping persi) per device Up/Partial abilitati, utilizzo dei link
  con sorgente (max(tx, rx) / SpeedBps), come media dei campioni grezzi sulla finestra (`ThresholdWindowMinutes`,
  default 5; almeno `MinSamples` campioni). Apertura sopra soglia, **chiusura sotto l'80%** (isteresi). Valori
  generali in `MonitoringSettings` (100 ms, 5 %, 80 %; null = disattivata); specifici su `Device.RttThresholdMs` /
  `LossThresholdPct` e `MapLink.UtilizationThresholdPct` (null = generale, 0 = disattivata). Stato in
  `OpenThresholdAlerts` (PK Kind+DeviceId+LinkId, Guid.Empty per i device); ogni transizione è un evento
  `ThresholdRaised`/`ThresholdCleared` con `AlertKey` ("latency", "loss", "link:{id}"); la chiusura prende in carico
  l'apertura. Notificati solo agli iscritti con filtro `AllProblems` ("Down, Partial e soglie").
- **Finestre di manutenzione** (`MaintenanceWindows`, pagina `/maintenance`, gestione Operatore/Admin): ambito
  tutto/cliente/mappa (con sottomappe)/device; singole (StartsAt–EndsAt) o settimanali (bit dei giorni, ora locale nel
  fuso delle notifiche, durata; scavalcano la mezzanotte, seguono l'ora legale: `MaintenanceSchedule`, coperto da
  test). Durante la finestra i problemi passano a `NotifyState.Maintenance` (rimandati), i rientri sono soppressi;
  alla fine della finestra i problemi ancora aperti tornano Pending e vengono notificati, quelli risolti o presi in
  carico diventano Suppressed. Mappa: bordo blu tratteggiato (`MapNodeDto.Maintenance`); Dispositivi: badge
  "manutenzione" (`DeviceOverviewDto.Maintenance`, servizio `ActiveMaintenance`).
- **Promemoria** (`NotificationSettings.ReminderMinutes`, 0 = disattivati, default 0; `ReminderIncludeWarnings`):
  problemi notificati, ancora aperti (né risolti né presi in carico, Down ancora Down, agente ancora offline) e non
  in manutenzione vengono rinotificati ("PROMEMORIA …, ancora aperto da X"; `NotificationDelivery.IsReminder`,
  `Event.LastReminderAt`/`ReminderCount`). **La presa in carico ferma i promemoria.**
- **Presa in carico** (`Event.Acknowledged`): segnala un problema già gestito; la vista "Solo da prendere in carico"
  è la lista di lavoro del NOC. **Chiusura automatica al ripristino**: quando un device torna Up da Down/Partial,
  `DeviceStatusService` chiude i suoi eventi di problema aperti (Acknowledged + `ResolvedAt`), nella stessa transazione
  del salvataggio (chiude anche un Down arrivato nello stesso report); idem per `AgentOffline` al ritorno online.
  Gli eventi Info nascono già presi in carico e nella pagina non hanno il bottone. "Riapri" azzera anche ResolvedAt.
- **UI**: Impostazioni → Notifiche (SMTP, Telegram, ritardo, fuso, indirizzo di VedettaVip, "Salva e invia prova");
  pagine **Contatti** (con iscrizioni), **Clienti**, **Eventi** (filtri per cliente, dispositivo, tipo, periodo e "da prendere in carico", presa in carico,
  esito degli invii con dettaglio per canale, tentativi ed errore); campo Cliente nel pannello del device.

### Impostazioni (solo Admin, `/settings/...`)

- `Components/SettingsShell`: sottomenu a sinistra (a schede sotto i 900 px) e titolo/azioni della pagina; dentro c'è
  già `AdminOnly`. Sezioni: **Monitoraggio** (`/settings` e `/settings/monitoring`: rilevazione dello stato, soglie delle
  metriche), **Profili SNMP**, **Profili RouterOS**, **WinBox** (percorso, gestore `winbox://`), **Notifiche** (SMTP, Telegram, invio), **Contatti**, **Clienti**, **Utenti**, **Dashboard**.
- Menu principale: Home, Mappa, Dispositivi, Eventi, Manutenzione, Impostazioni. Le vecchie route `/users`,
  `/customers`, `/contacts` restano valide e attivano la stessa voce del sottomenu.
- Una nuova sezione: pagina con `<SettingsShell Title="...">` e voce in `SettingsShell.Sections`.

### Modalità modifica della mappa (implementata, `/map`)

- Default **sola visualizzazione** (monitor NOC): trascinare un nodo o lo sfondo sposta la vista, niente modifiche.
- Bottone **Modifica** → strumenti **Sposta** (trascina nodi, snap e PUT della posizione al rilascio) e
  **Collega** (trascina da un nodo a un altro; in Sposta anche con **Shift + trascina**). Il link nasce a 1 Gbps
  senza sorgente di traffico e resta selezionato per completarlo.
- **Clic destro su un nodo** (anche fuori da Modifica): menu contestuale (`NodeMenu`, vedi "Apri con WinBox").
- **Clic destro** su un punto vuoto (o "Usa il centro della vista") → mirino e form "Aggiungi nodo":
  dispositivo non ancora sulla mappa, sottomappa (mappe radice esistenti o nuova mappa), nodo statico.
- **Clic** su nodo o link → pannello laterale (`Components/MapEditPanel`): template dell'etichetta; per i link
  velocità (preset 100M…100G), device + IfIndex sorgente del traffico (proposti i device dei due estremi),
  Inverti (scambia From/To, cioè tx/rx), Elimina con conferma al secondo clic (`DeleteButton`).
- Errori dell'API mostrati in chiaro: `ApiProblemException` (in `VedettaVipApiClient.cs`) estrae title/detail/errori
  di validazione dal ProblemDetails.
- JS: `Components/NetworkMap.razor.js` (`getBoundingClientRect`, per convertire il clic destro in coordinate mappa) e
  `Components/NodeMenu.razor.js` (menu dentro la finestra, copia negli appunti). Pan, drag e link usano solo i delta del
  puntatore.
- **Modifiche dal vivo negli altri browser** (`Services/MapNotifier` nell'API, coperto da test): dopo ogni scrittura riuscita
  su mappe (crea, rinomina, elimina), nodi (crea, etichetta/icona, posizione, sposta, elimina) e link, l'API invia
  `MapsChanged` (`MapsChangedDto`: MapIds + ClientId) su `/hubs/status`. Alle mappe toccate si aggiungono le **antenate**
  (i loro nodi Submap mostrano stato aggregato e membri); spostare o agganciare/staccare una sottomappa include anche la
  sottomappa (cambia la mappa padre). Lo inviano anche: modifica di un device (mappe in cui è un nodo), creazione con
  `?mapId=`, Scoperta "Aggiungi selezionati", import CSV (mappe dei device creati o aggiornati) e finestre di
  manutenzione (MapIds null = tutte le mappe, bordo dei nodi in manutenzione).
  - **Mittente**: ogni scheda del browser genera un id (`ApiCredentialsHandler.ClientId`) e lo manda in ogni chiamata con
    l'header `X-VedettaVip-Client` (ammesso dal CORS; nel messaggio solo se `[A-Za-z0-9-]{1,64}`). La scheda che ha fatto
    la modifica la ignora: ha già il risultato.
  - **Client** (`Pages/Map`): il selettore delle mappe si aggiorna sempre; la mappa aperta, se è tra quelle cambiate, con
    una ricarica "morbida" (`RefreshFromServerAsync`) che conserva vista, zoom, selezione, pannello di modifica (il form
    si azzera solo se cambia la selezione) e grafici, se nodo o link esistono ancora. Attesa di 300 ms per raccogliere i
    messaggi di una stessa operazione; durante il trascinamento di un nodo o di un link (`NetworkMap.IsInteracting`) la
    ricarica aspetta il rilascio. Mappa eliminata da un altro utente: si apre la mappa iniziale con un avviso.
  - Una notifica persa non è grave: alla riconnessione dell'hub la mappa viene comunque ricaricata.
- **"+ Nuova mappa"** nell'intestazione (Admin/Operatore, anche fuori da Modifica e senza mappe): crea una mappa
  principale e la apre.
- **Selettore delle mappe** nell'intestazione: tutte le mappe ad albero (radici per nome, sottomappe rientrate, numero
  di nodi). **Mappa iniziale** di `/map`: l'ultima aperta su quel browser (`localStorage`, chiave `vedettavip.lastMap`) se
  esiste ancora, altrimenti la radice con più nodi (a parità, per nome).
- **Gestione della mappa corrente** in modifica: Rinomina (ripassa sfondo e griglia invariati),
  Elimina con conferma (nodi e link compresi; 409 se è ancora una sottomappa; le sue sottomappe diventano radici).

### Icone (implementate)

- **Tabler Icons** (MIT), sottoinsieme di 43 icone outline (39 per nodi e dispositivi, 4 per i comandi: `UiIcons`) in `third-party/tabler-icons/` (`LICENSE`, `VERSION`, `icons/`,
  `generate.py`). Lo script genera `VedettaVip.Web.Client/Services/MapIcons.g.cs` (chiave, etichetta, contenuto SVG):
  incorporate nel client, nessuna dipendenza da internet in esecuzione. Per aggiungerne una vedere il commento in `generate.py`.
- **Icone dei comandi** (`UI_ICONS` in `generate.py` → `Services/UiIcons.g.cs`, componente `UiIcon`): bottoni a sola icona
  (`.btn-icon` in `app.css`, testo in `title` e `aria-label`; `DeleteButton IconOnly`). Pagina Dispositivi: Grafici, WinBox
  (posto vuoto `.btn-icon-slot` sulle righe senza, per tenere le colonne allineate), Modifica, Elimina.
- `Services/MapIcons`: icona effettiva = quella del nodo (`MapNode.Icon`, `MapNodeDto.OwnIcon`) → del dispositivo
  (`Device.Icon`) → predefinita del tipo (`DefaultFor`; sottomappe `sitemap`, nodi statici nessuna). Chiave sconosciuta =
  predefinita. Sulla mappa solo le icone usate diventano `<symbol>` nei defs, il nodo le richiama con `<use>` a sinistra
  dell'etichetta (colore del riquadro = stato).
- Scelta: `Components/IconPicker` (griglia + "auto") nel pannello del dispositivo e nel pannello del nodo (modalità
  Modifica); `MapIconView` per le icone inline (colonna Tipo della pagina Dispositivi).
- `DeviceType` esteso: Router, Switch, AccessPoint, Server, Firewall, Storage, Pc, Printer, Camera, Phone, Ups, Other
  (testo nel DB, alias italiani nell'import CSV). Migration `StaticInternetCloudIcon`: nodi statici "Internet" → `cloud`.
- I test includono solo i sorgenti `MapIcons*.cs` (Compile Link), non il progetto client: un ProjectReference al client
  WebAssembly lo ricompilerebbe e romperebbe gli hash degli asset del Web host in esecuzione.

### Grafici del traffico (implementati)

- `Components/InterfaceTrafficChart` (Blazor-ApexCharts 6.0.2, `AddApexCharts()` nel client): due aree tx/rx orientate
  come il link (stessa regola `TxIsIn` della mappa), periodi 1h/6h/24h/7g/30g, statistiche media, picco (massimo
  dei campioni), 95° percentile (sulle medie dei bucket) e picco % sulla velocità rilevata. X in ms epoch con
  `DatetimeUTC = false` (ora locale del browser). Aggiornamento ogni 60 s per i periodi fino a 24 h.
- `Components/DeviceLatencyChart`: RTT medio (area) e max (linea) in ms, perdita % a barre sull'asse destro;
  statistiche RTT medio/max/95° percentile, perdita, disponibilità. Base comune `TimeRangeChartBase` (periodi,
  aggiornamento, errori) e `ChartRangeToolbar`; le query SQL di traffico e latenza sono generate dalla stessa
  descrizione delle colonne (`MetricQuery`).
- `Components/DeviceCharts`: latenza + traffico delle interfacce con storico (Out = uscita, In = ingresso).
  Pagina Dispositivi: bottone **Grafici** per riga. Mappa: **doppio clic su un device** apre gli stessi grafici.
- Mappa: in sola visualizzazione un **clic su un link** (senza trascinare) apre il grafico sotto la mappa; in
  modifica bottone "Grafico" nel pannello del link.

### Dashboard (implementata, Home `/`)

- Pagina WebAssembly `Pages/Home` (la vecchia Home statica di VedettaVip.Web è stata rimossa), aggiornata ogni 30 s da
  `GET /api/devices/overview`, `/api/agents` e `/api/events?since=`.
- **Stato dei dispositivi**: ciambella SVG (Up/Partial/Down/Sconosciuti, solo device abilitati, % up al centro) con
  legenda, conteggi di manutenzione, disabilitati e totale.
- **Dispositivi con problemi**: Down prima dei Partial, poi i più recenti; da quando (data e durata), cliente,
  badge manutenzione, link alle mappe in cui compaiono.
- **Eventi recenti**: periodo predefinito in Impostazioni → Dashboard (`DashboardEventHours`), selettore al volo
  (1 h … 7 giorni), massimo 500 eventi. Banner se un agente è offline.

### Pagina dispositivi (implementata, `/devices`)

- Tabella da `GET /api/devices/overview`, aggiornata ogni 30 s: stato (stessi colori della mappa), nome, indirizzo,
  tipo, SNMP (versione + ok/ko), RTT, padre, mappe in cui compare (link diretti). Riepilogo per stato in testata.
- Ricerca per nome/indirizzo e filtri: stato (tutti, solo problemi, sconosciuti, disabilitati), **cliente** (anche "senza
  cliente") e **mappa** (ad albero; una mappa comprende le sue sottomappe, `MapHierarchy.Subtree`; anche "su nessuna
  mappa"). Cliente e mappa stanno nell'indirizzo (`/devices?customer=…&map=…`, `SupplyParameterFromQuery`): link e
  segnalibri riaprono la stessa vista. "N di M" e "Azzera filtri".
- Pannello `Components/DeviceEditor` (420 px) a **schede**: Generale (nome, indirizzo, tipo, stato, cliente, padre, mappa
  alla creazione), Monitoraggio (SNMP, profilo SNMP, soglie di rilevazione e delle metriche), RouterOS (API e profilo),
  Icona. Badge sulla scheda con valori personalizzati; un solo Salva per tutte le schede.
  Alla creazione "Aggiungi alla mappa" (mappe ad albero, `Services/MapHierarchy`): il nodo va **sempre sotto i nodi
  esistenti** (`MapPlacement.NextBelow` nell'API, coperto da test): mappa vuota in alto a sinistra, poi una "riga dei
  nuovi arrivi" sotto il nodo più basso, continuata verso destra finché i suoi nodi restano sulle celle della griglia
  (8 per riga); se vengono spostati, il successivo apre una riga nuova. La topologia disegnata a mano non viene toccata.
  Il padre esclude il device stesso e i suoi discendenti; avviso se l'indirizzo è già usato da un altro device.
  **Icon e SnmpCredentialId vengono ripassati invariati** nel PUT (il DTO li sovrascriverebbe con null).
- Eliminazione con conferma; il 409 mostra le mappe in cui il device è in uso (va tolto prima da lì). Se bloccano
  solo gli eventi, l'avviso propone di disabilitarlo (lo storico resta) oppure "Elimina comunque (cancella anche
  N eventi)" con conferma: `DELETE ...?purgeEvents=true`, eventi e device nella stessa transazione.
- Badge "soglie" sui device con soglie di rilevazione specifiche (tooltip con i valori).
- **Import/export CSV** (`Components/DeviceImportPanel`, `Services/DeviceCsv` + `Endpoints/DeviceImportEndpoints` nell'API,
  parser coperto da test): colonne nome, indirizzo, tipo, produttore, snmp, cliente, padre, profilo_snmp, abilitato, mappa (alias italiani e
  inglesi, separatore `;` `,` o tab rilevato dall'intestazione, UTF-8 o Latin-1). Anteprima riga per riga (nessuna scrittura)
  e conferma in un unico SaveChanges; duplicati per indirizzo saltati o aggiornati (una colonna presente sovrascrive anche se
  vuota); padre per indirizzo o nome, anche di una riga del file, con controllo dei cicli; clienti mancanti creati solo se
  Admin lo chiede. **Mappa** per riga (`Services/MapReferences`, coperto da test): nome senza distinzione di maiuscole, o
  percorso `Padre/Figlia` se il nome non è univoco, più mappe separate da `|`; cella vuota o colonna assente = mappa scelta
  nel pannello (o nessuna); mappa inesistente = errore della riga (le mappe non si creano dall'import). Aggiunge soltanto,
  mai toglie: un device già sulla mappa non viene duplicato. Nodi a griglia sotto quelli esistenti, mappa per mappa.
  L'export scrive le mappe di ogni device (nome, o percorso se ambiguo). Massimo 2000 righe / 2 MB.

## Requisiti della mappa (priorità massima)

1. Nodi colorati per stato: Up verde, Partial arancione, Down rosso, Unknown grigio.
2. Etichette a template con valori live, es. `[Name]\n[Address]\nCPU: [Cpu]%`
   (classe `LabelTemplate` in VedettaVip.Shared).
3. Link disegnati come due metà (tx e rx), spessore e colore in base all'utilizzo
   (< 50% verde, < 80% giallo, oltre rosso), testo tx/rx al centro.
4. Drag & drop dei nodi con snap alla griglia; pan con trascinamento dello sfondo; zoom con la rotella.
5. Sottomappe: doppio clic per aprirle; lo stato del nodo-sottomappa è il **peggiore**
   tra i nodi della mappa figlia e delle sue discendenti (calcolato lato server, aggiornato dal vivo nel client). **Fatto.**
6. Salvataggio della posizione al rilascio del nodo (PUT verso l'API).
7. Modalità modifica / sola visualizzazione (per monitor NOC). **Fatto.**
8. Icone per tipo di dispositivo tramite `<symbol>` SVG nei `<defs>`. **Fatto** (Tabler Icons, vedi "Icone").
9. Sfondo personalizzabile (planimetria o mappa geografica).
10. Auto-layout force-directed (Fruchterman-Reingold) solo per i nodi nuovi dopo la
    discovery; i nodi già posizionati a mano non si spostano.

## Protocolli e porte di riferimento

| Uso                         | Protocollo / porta                             |
|-----------------------------|------------------------------------------------|
| Raggiungibilità             | ICMP echo                                      |
| SNMP                        | UDP 161 (trap: UDP 162)                        |
| RouterOS API                | TCP 8728 (in chiaro), 8729 (TLS)               |
| RouterOS REST (v7)          | HTTPS `/rest` sul servizio www-ssl             |
| MikroTik Neighbor Discovery | UDP 5678 (MNDP, formato TLV)                   |
| Winbox                      | TCP 8291: protocollo proprietario, **NON usare** (si avvia solo il client WinBox 4 via `winbox://`) |

OID utili:
- `sysUpTime` 1.3.6.1.2.1.1.3.0
- `ifHCInOctets` 1.3.6.1.2.1.31.1.1.1.6
- `ifHCOutOctets` 1.3.6.1.2.1.31.1.1.1.10
- `ifHighSpeed` 1.3.6.1.2.1.31.1.1.1.15 (Mbps)

## Convenzioni di codice

- File-scoped namespaces, primary constructors dove ha senso, `record` per i DTO immutabili.
- Tutto asincrono, con `CancellationToken` propagato ovunque; mai `.Result` o `.Wait()`.
- Polling con parallelismo limitato (`Parallel.ForEachAsync` + `MaxDegreeOfParallelism`)
  e timeout espliciti per ogni probe.
- **Numeri nell'SVG sempre con `CultureInfo.InvariantCulture`**: con cultura it-IT
  i decimali hanno la virgola e l'SVG si rompe.
- Contatori interfacce: usare sempre quelli a 64 bit (`ifHC*`) e gestire il reset
  del contatore (valore corrente < precedente, ad esempio dopo un riavvio) scartando il campione.
- Gli aggiornamenti dello stato UI da thread di background passano da `InvokeAsync(...)`.
- Il componente mappa usa `ShouldRender` per evitare render inutili; oltre qualche
  centinaio di nodi ogni nodo diventa un sotto-componente con il proprio `ShouldRender`.
- Logging strutturato con `ILogger<T>`; niente `Console.WriteLine` nel codice definitivo.
- La logica dei componenti Razor va nei file code-behind `.razor.cs`; nel `.razor` solo markup.
  Motivo: nei blocchi `@code` le righe che iniziano con `<` vengono interpretate come tag HTML.

## Insidie note

- **ApexCharts.js non è più open source da 5.1.0** (luglio 2025: doppia licenza, gratuita solo sotto 2 M$ di fatturato,
  niente sublicenza). Blazor-ApexCharts 6.1.0 e successive includono ApexCharts.js 5.3+/6.x: incompatibili con la
  pubblicazione AGPL. Restare su **Blazor-ApexCharts 6.0.2** (ApexCharts.js 4.7.0, MIT) o sostituire la libreria;
  prima di aggiornare qualsiasi dipendenza JS verificare la licenza del file effettivamente incluso.

- **Blazor Server e drag**: ogni `pointermove` è un round-trip SignalR, quindi il drag è fluido
  in LAN ma con lag su VPN/internet. Il componente mappa deve girare in WebAssembly
  (progetto VedettaVip.Web.Client); i dati live arrivano comunque via SignalR.
- **Tempeste di alert**: senza dipendenze parent/child la caduta di un router di sede genera
  un alert per ogni dispositivo dietro. Le dipendenze vanno implementate insieme all'alerting.
- **Isteresi**: un dispositivo è Down solo dopo N poll falliti consecutivi (default 3).
- **SNMP timeout ≠ Down**: un host che risponde al ping ma non a SNMP è Partial, non Down.
- **SNMP request-id negativi e RouterOS**: `Messenger.GetAsync` di SharpSnmpLib usa un contatore con seed casuale,
  quindi in circa metà degli avvii gli id sono negativi. RouterOS li rimanda codificati su 5 byte (`02 05 FF …`, BER
  non minimale) e SharpSnmpLib scarta la risposta ("data construction exception" / "Truncation error for 32-bit
  integer coding"): device **sempre Partial** pur con SNMP funzionante. `SnmpProbe` costruisce la richiesta con
  `NextRequestId()` sempre positivo e `GetResponseAsync`; non tornare a `Messenger.GetAsync`.
- **Apparati senza ifXTable** (es. DrayTek Vigor2912 firmware 3.7.8: solo MIB-II): niente ifName/ifAlias/ifHighSpeed né
  contatori a 64 bit. `SnmpProbe` se ne accorge al primo poll (nessun Counter64) e per quell'indirizzo usa ifTable:
  `ifInOctets`/`ifOutOctets` a 32 bit, `ifSpeed`, `ifDescr` (riprova ifXTable ogni ora). `TrafficCalculator` corregge il
  giro dei contatori a 32 bit (2³² byte, ~6 min a 100 Mbit/s) se la velocità risultante è plausibile (≤ 1,5 × ifSpeed),
  altrimenti lo tratta come reset. L'inventario prende il nome da `ifDescr` e la velocità da `ifSpeed` quando mancano.
- **Indirizzi "irraggiungibili" per i test**: in alcune LAN il gateway risponde anche per indirizzi privati inesistenti
  (es. tutto 10.0.0.0/8). Per simulare un device giù usare TEST-NET `192.0.2.0/24` (RFC 5737).
- **Client SignalR in file-based app (`dotnet run x.cs`)**: la serializzazione JSON via reflection è
  disattivata; aggiungere `#:property JsonSerializerIsReflectionEnabledByDefault=true`.
- **JS collocato nel progetto client**: `NetworkMap.razor.js` è servito come `Components/NetworkMap.razor.js`
  (alla radice del sito, **senza** il prefisso `_content/VedettaVip.Web.Client/` delle RCL).
- **`dotnet build` con il Web avviato**: sovrascrive gli asset WASM sotto il processo in esecuzione
  (errore "Failed to load module script" nel browser): riavviare VedettaVip.Web dopo ogni build.
  Se dopo il riavvio il browser segnala "SRI's integrity checks failed" su `VedettaVip.Web.Client.*.pdb`: il .pdb è
  in `force-cache` e può mantenere lo stesso nome tra build diverse, quindi svuotare la cache (Ctrl+Shift+R).
- **Pagina che scorre in orizzontale**: `main` nel layout ha `min-width: 0` (un elemento flex non si restringe sotto il
  contenuto); le tabelle larghe vanno in un contenitore con `overflow-x: auto`.
- **`<select>` legato a un `bool` in Blazor**: non si aggancia in modo affidabile (la scelta non arriva al campo). Usare
  un campo stringa (es. `"api"` / `"api-ssl"`) e una proprietà derivata.
- **Non compilare solo `VedettaVip.Web.Client`**: il manifest degli asset (hash di integrità) sta nel progetto host
  `VedettaVip.Web`; compilando solo il client il browser rifiuta il nuovo `dotnet.*.js` ("Failed to fetch dynamically
  imported module") e la mappa non parte. Compilare la solution o `src/VedettaVip.Web`.

## Roadmap

- [x] Prototipo mappa SVG (drag, snap, pan, zoom, stati, link tx/rx, template etichette),
      integrato in VedettaVip.Web.Client (pagina `/map`, InteractiveWebAssembly, voce "Mappa" nel NavMenu).
- [x] Creazione della solution (`VedettaVip.sln`) con la struttura sopra, riferimenti a VedettaVip.Shared, `dotnet build` ok.
- [x] `deploy/docker-compose.dev.yml` con servizio TimescaleDB (`timescale/timescaledb:latest-pg17`,
      container `vedettavip-db`, progetto `vedettavip-dev`, porta solo su 127.0.0.1, healthcheck `pg_isready`);
      credenziali in `deploy/.env.dev` (non committato, modello in `deploy/.env.dev.example`), override locale facoltativo
      `deploy/docker-compose.dev.override.yml` (aggiunto da `dev.sh`).
- [x] Deploy di produzione (`deploy/docker-compose.yml` + `deploy/production/`, vedi il README lì): Dockerfile di API (con target `migrator`,
      bundle EF), Worker (non root, `CAP_NET_RAW` sul binario dotnet) e Web; compose con TimescaleDB fissato
      (2.30.2-pg17), migrate, api, worker, web e Caddy (`tls internal`, `default_sni`, `/api` e `/hubs` → API);
      `setup-server.sh` (Docker, ufw) e `deploy.sh <host-ssh>` o `deploy.sh --config <cartella>` (copia dei file del
      repository; `.env` generato una volta in `/opt/vedettavip/vedettavip.env` oppure copiato con override da una cartella
      di configurazione esterna, con controlli su segnaposto, password del DB e volumi; `compose.sh` sul server; build,
      avvio, riavvio di Caddy). Porte di Caddy configurabili (`HTTP_PORT`, `HTTPS_PORT`).
      Volume `web-keys` per le chiavi di Data Protection del Web (antiforgery: senza, a ogni deploy i cookie dei browser
      non si decifrano). Backup notturno (`backup.sh` + `vedettavip-backup.timer`: dump verificato, vedettavip.env, CA di Caddy, conservazione
      14 giorni, copia facoltativa su NAS montato con `setup-nas.sh`: share SMB in automount, credenziali in
      `/root/.vedettavip-nas.cred`) e `restore.sh` (`--test` in un DB separato, `--yes` ripristino vero). Il container del Worker richiede `cap_add: [NET_RAW]` per l'ICMP; chiavi e connection
      string via variabili d'ambiente (`Agent__ApiKey`, `ConnectionStrings__VedettaVip`).
- [x] Modello dati EF Core (Npgsql) + migration `InitialCreate` con seed del prototipo + API minimal
      (mappe, nodi con invariante sottomappe, CRUD device con 409 se in uso) + CORS per VedettaVip.Web.
- [x] Mappa caricata dall'API nel client: `/map` (mappa iniziale: ultima usata, poi radice con più nodi) e `/map/{id}`, PUT della posizione
      al rilascio, doppio clic sulla sottomappa per aprirla, link alla mappa padre. Traffico e CPU ancora
      simulati. Rimosse le pagine di esempio Counter e Weather.
- [x] Endpoint CRUD per mappe e link (`MapEndpoints`, `MapLinkEndpoints`); più link tra la stessa coppia
      di nodi sono ammessi (uplink ridondanti).
- [x] Modalità modifica / sola visualizzazione sulla mappa: aggiunta nodi (clic destro), link trascinando tra
      nodi, proprietà ed eliminazione dal pannello laterale, `PUT` di etichetta e icona del nodo.
      Modifiche propagate dal vivo agli altri browser (`MapsChanged`).
- [x] Pagina di gestione dispositivi `/devices` (elenco con stato, ricerca, filtri, crea/modifica/elimina).
- [x] Autenticazione utenti (account locali, ruoli Admin/Operatore/Lettura, setup del primo admin con codice nel
      log, blocco tentativi, CSRF, gestione utenti), verifica in due passaggi TOTP facoltativa con codici di recupero.
      Da fare: login esterno OIDC (Entra ID/Keycloak),
      limitazione per cliente (multi-tenant).
- [x] Poller ICMP + SNMP base (Worker senza accesso al DB, API key agent, isteresi, buffer e backoff),
      DeviceStatus + Event a ogni cambio, stato live via SignalR sulla mappa, tracciamento agenti con
      banner "agente offline". Community SNMP dai profili SNMP (Impostazioni), con ripiego sulla configurazione del Worker.
- [x] Traffico reale sui link (DeviceId + IfIndex, delta contatori ifHC* su sysUpTime, live via SignalR).
      Interfaccia scelta per nome da un combo (inventario SNMP in `DeviceInterfaces`).
- [x] Metriche su TimescaleDB (hypertable + aggregati 5 min/1 h, compressione, retention 7 g/90 g/2 anni) e grafici
      del traffico dei link (ApexCharts), latenza e perdita dei ping per device (pagina Dispositivi e doppio clic
      sulla mappa).
- [x] Integrazione RouterOS, prima tappa: API classica (api/api-ssl, porta per profilo), CPU, memoria, temperatura,
      tensione, uptime, versione; live sulla mappa, pagina Dispositivi, grafico.
- [x] RouterOS, seconda tappa: soglie CPU/temperatura, sorveglianza di interfacce e peer WireGuard con avvisi e notifiche.
      Da fare: IPsec, MNDP.
- [x] "Apri con WinBox": produttore dei device (MikroTik), menu contestuale del nodo, link `winbox://` senza credenziali,
      installer del gestore per Windows e Linux con il percorso delle Impostazioni. Da fare: macOS.
- [x] Discovery, prima tappa: vicini (RouterOS /ip/neighbor, LLDP-MIB, CDP-MIB) → link mancanti e dispositivi nuovi da
      confermare (pagina Scoperta).
- [x] Discovery, seconda tappa: tabella ARP dei router (con lease DHCP) e scansione di subnet (ping, DNS, SNMP, porte),
      proposte unite per IP, produttore dal MAC (OUI IEEE), "Ignora selezionati". Da fare: auto-layout.
- [x] Sottomappe con stato aggregato (ricorsivo, live via SignalR, badge e riepilogo nell'etichetta).
- [x] Soglie di rilevazione (ping persi per Down, riusciti per Up, errori SNMP per Partial) configurabili dalla UI:
      generali (pagina Impostazioni) e per singolo device.
- [x] Notifiche email (MailKit, SMTP configurabile dalla UI) e Telegram, destinatari per cliente/mappa/tutto, outbox
      con ritentativi, dipendenze (soppressione dei figli), flap, agente offline/online, pagina Eventi con presa in carico.
- [x] Soglie su latenza, perdita e utilizzo dei link (con isteresi), finestre di manutenzione singole e settimanali,
      promemoria dei problemi aperti (migration `AddThresholdsMaintenanceReminders`). Manca: webhook.
- [x] Icone per tipo di dispositivo (Tabler Icons, scelta per device e per nodo). Da fare: sfondi.
- [x] Preparazione alla pubblicazione open source (branch `release/public-prep`): nessun dato di installazione nel
      repository (configurazione da `.env`, override del compose, `appsettings.Local.json`; `deploy.sh --config`), licenza
      AGPL-3.0-or-later con NOTICE 7(b), intestazioni SPDX, footer e pagina `/about`, THIRD-PARTY-NOTICES.md (Blazor-ApexCharts
      fermo a 6.0.2 per la licenza di ApexCharts.js), README in inglese e italiano con screenshot del seed, CONTRIBUTING (DCO),
      SECURITY; verificato con un clone pulito (build, test, Docker Compose da `.env.example`) e gitleaks.
- [x] Pubblicazione: repository pubblico nato da un solo commit iniziale ("VedettaVip 0.1.0"); la configurazione di
      un'installazione (`.env`, `docker-compose.override.yml`, eventuale `appsettings.Local.json`) sta in una cartella
      esterna passata a `deploy.sh --config`, tenuta in un repository git privato e solo locale.
- [ ] Agenti remoti e multi-tenant per cliente (scenario MSP).

## Comandi utili

```bash
# build e test
dotnet build
dotnet test

# avvio in sviluppo: tutto in un terminale (DB, build, API, Worker, Web; Ctrl+C ferma tutto)
./dev.sh

# oppure in tre terminali ( il Worker usa Agent:ApiKey dai propri user-secrets)
dotnet run --project src/VedettaVip.Api --launch-profile http
dotnet run --project src/VedettaVip.Worker
dotnet run --project src/VedettaVip.Web --launch-profile http     # poi http://localhost:5032/map

# database di sviluppo (prima volta: cp deploy/.env.dev.example deploy/.env.dev e cambiare la password; dev.sh lo avvia da solo)
docker compose -f deploy/docker-compose.dev.yml --env-file deploy/.env.dev up -d
docker exec -it vedettavip-db psql -U netmap -d netmap

# stack completo in Docker (cp deploy/.env.example deploy/.env e compilarlo)
docker compose -f deploy/docker-compose.yml up -d --build

# chiave agent condivisa API/Worker (una volta, senza stamparla)
K=$(openssl rand -base64 32); dotnet user-secrets set "Agent:ApiKey" "$K" --project src/VedettaVip.Api >/dev/null
dotnet user-secrets set "Agent:ApiKey" "$K" --project src/VedettaVip.Worker >/dev/null; unset K

# connection string di sviluppo (una volta; password da deploy/.env.dev)
dotnet user-secrets set "ConnectionStrings:VedettaVip" \
  "Host=127.0.0.1;Port=5432;Database=netmap;Username=netmap;Password=<password>" --project src/VedettaVip.Api

# migrations EF Core (cartella src/VedettaVip.Api/Data/Migrations)
dotnet ef migrations add <Nome> --project src/VedettaVip.Api --output-dir Data/Migrations
dotnet ef database update --project src/VedettaVip.Api
dotnet ef migrations script --idempotent --project src/VedettaVip.Api -o deploy/migrate.sql   # per la produzione
```

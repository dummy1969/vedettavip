# Third-party notices

VedettaVip is licensed under the GNU AGPL v3.0 or later (see `LICENSE` and `NOTICE`). It uses the third-party components
listed below, each under its own license. Versions are those resolved by the build (`dotnet list package
--include-transitive`) and those of the files shipped in the repository; licenses were read from the package metadata
(`.nuspec`), from the files themselves or from the upstream repositories.

**AGPL compatibility.** MIT, Apache-2.0 and the PostgreSQL License are permissive licenses compatible with the GNU
AGPLv3 (Apache-2.0 is compatible with version 3 of the GPL family, not with version 2). Components marked *separate
program* are not linked with VedettaVip: they run as independent processes (Docker images) and talk to it over the
network, so their licenses do not need to be compatible with the AGPL, but you must accept them to run the stack.

## Bundled in the applications (runtime)

### Server (VedettaVip.Api, VedettaVip.Worker, VedettaVip.Web)

| Component | Version | License | Used by | AGPLv3 |
|---|---|---|---|---|
| .NET runtime, ASP.NET Core, Blazor, SignalR, Microsoft.Extensions.* | 10.0.12 | MIT | all | compatible |
| Microsoft.EntityFrameworkCore, .Relational, .Abstractions | 10.0.12 | MIT | Api | compatible |
| Microsoft.AspNetCore.Identity.EntityFrameworkCore | 10.0.12 | MIT | Api | compatible |
| Microsoft.AspNetCore.DataProtection.EntityFrameworkCore | 10.0.12 | MIT | Api | compatible |
| Microsoft.AspNetCore.OpenApi | 10.0.12 | MIT | Api | compatible |
| Microsoft.OpenApi | 2.12.0 | MIT | Api | compatible |
| Microsoft.AspNetCore.SignalR.Client | 10.0.12 | MIT | Worker, Web | compatible |
| Microsoft.AspNetCore.Components.WebAssembly.Server | 10.0.12 | MIT | Web | compatible |
| Npgsql | 10.0.3 | PostgreSQL License | Api | compatible |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 | PostgreSQL License | Api | compatible |
| MailKit | 4.18.1 | MIT | Api | compatible |
| MimeKit | 4.18.1 | MIT | Api | compatible |
| BouncyCastle.Cryptography | 2.7.0 | MIT | Api (via MimeKit) | compatible |
| Net.Codecrete.QrCodeGenerator | 3.2.1 | MIT | Api (two-factor setup QR code) | compatible |
| Lextm.SharpSnmpLib | 12.5.7 | MIT | Worker | compatible |
| System.Security.Cryptography.Pkcs | 10.0.0 | MIT | Api | compatible |
| System.Diagnostics.EventLog | 10.0.12 | MIT | Worker | compatible |

### Browser (VedettaVip.Web.Client, WebAssembly, and static files)

| Component | Version | License | Notes | AGPLv3 |
|---|---|---|---|---|
| .NET WebAssembly runtime, Blazor, Microsoft.JSInterop, Microsoft.Extensions.* | 10.0.12 | MIT | | compatible |
| Blazor-ApexCharts | 6.0.2 | MIT | NuGet package | compatible |
| ApexCharts.js | 4.7.0 | MIT | bundled in Blazor-ApexCharts 6.0.2 (`apexcharts.esm.js`) | compatible |
| Bootstrap | 5.3.3 | MIT | `src/VedettaVip.Web/wwwroot/lib/bootstrap` | compatible |
| Tabler Icons (subset of 43 outline icons) | 3.49.0 | MIT | `third-party/tabler-icons`, compiled into `MapIcons.g.cs` and `UiIcons.g.cs` | compatible |

> **Do not upgrade Blazor-ApexCharts past 6.0.2 without checking the bundled ApexCharts.js.** ApexCharts.js 5.1.0 and
> later (Blazor-ApexCharts 6.1.0 bundles 5.3.6, 7.0.0 bundles 6.5.0) are distributed under the "ApexCharts License",
> a dual license that is free only for organizations under USD 2M annual revenue and forbids sublicensing. It is not an
> open source license and is **not compatible** with distributing VedettaVip under the AGPL.

### Data

| Data | Version | Terms | Notes |
|---|---|---|---|
| IEEE Registration Authority public listings (MA-L, MA-M, MA-S) | see `third-party/ieee-oui/VERSION` | public data published by the IEEE at standards-oui.ieee.org, subject to the IEEE website terms of use | reduced to "prefix → organization name" in `src/VedettaVip.Api/Resources/oui.tsv.gz` by `third-party/ieee-oui/generate.py`. The listings are facts (assignments of MAC prefixes) and carry no software license; the IEEE is not affiliated with VedettaVip. |

## Separate programs (Docker images used by `deploy/docker-compose.yml`)

| Component | Version | License | Notes |
|---|---|---|---|
| PostgreSQL | 17 | PostgreSQL License | included in the TimescaleDB image |
| TimescaleDB (`timescale/timescaledb`) | 2.30.2-pg17 | Apache-2.0 (core) + **Timescale License (TSL)** | see below |
| Caddy (`caddy`) | 2.11.6 | Apache-2.0 | HTTPS reverse proxy |
| .NET container images (`mcr.microsoft.com/dotnet/sdk`, `aspnet`, `runtime-deps`) | 10.0 | MIT (.NET); Debian packages under their own licenses | base images of the VedettaVip images; the API image adds `libgssapi-krb5-2` (MIT Kerberos license) |

### TimescaleDB and the Timescale License

TimescaleDB is open core: the code outside the `tsl` directory is Apache-2.0, the code inside it (and the binaries with
`-tsl` in their name) is under the **Timescale License** ([text](https://github.com/timescale/timescaledb/blob/main/tsl/LICENSE-TIMESCALE)).
The official `timescale/timescaledb` image runs with `timescaledb.license = timescale`, and VedettaVip relies on TSL
features: **compression, retention policies and continuous aggregates** (migration `AddMetrics`).

The Timescale License is not an open source license. In short (read the full text, this is not legal advice): it allows
internal use and use as the backend of a "value added" product or service, and it prohibits offering TimescaleDB
itself as a database-as-a-service or software-as-a-service in which users access the database. VedettaVip does not
include or link TimescaleDB: the database is a separate program, pulled from Docker Hub by the operator, so the AGPL
of VedettaVip and the TSL of the database do not conflict. Whoever runs a VedettaVip installation is responsible for
complying with the TSL.

## Build and test only (not shipped)

| Component | Version | License |
|---|---|---|
| Microsoft.EntityFrameworkCore.Design (+ Microsoft.CodeAnalysis.*, Microsoft.Build.Framework, Humanizer.Core, Mono.TextTemplating, System.CodeDom, System.Composition.*, Newtonsoft.Json, Microsoft.VisualStudio.SolutionPersistence) | 10.0.12 (Design) | MIT |
| dotnet-ef (migration bundle, in the API Dockerfile) | 10.0.12 | MIT |
| Microsoft.NET.ILLink.Tasks, Microsoft.NET.Sdk.WebAssembly.Pack, Microsoft.DotNet.HotReload.WebAssembly.Browser | 10.0.12 / 10.0.112 | MIT |
| xunit, xunit.core, xunit.assert, xunit.analyzers, xunit.extensibility.*, xunit.runner.visualstudio | 2.9.3 / 1.18.0 / 3.1.4 | Apache-2.0 |
| xunit.abstractions | 2.0.3 | Apache-2.0 (license URL in the package metadata) |
| Microsoft.NET.Test.Sdk, Microsoft.TestPlatform.*, Microsoft.CodeCoverage | 17.14.1 | MIT |
| coverlet.collector | 6.0.4 | MIT |
| Microsoft.EntityFrameworkCore.InMemory | 10.0.12 | MIT |

## License texts

- MIT: each component's license file (e.g. `third-party/tabler-icons/LICENSE`, the header of `bootstrap.min.css`, the
  `LICENSE` file inside each NuGet package).
- Apache-2.0: <https://www.apache.org/licenses/LICENSE-2.0>
- PostgreSQL License: <https://www.postgresql.org/about/licence/>
- Timescale License: <https://github.com/timescale/timescaledb/blob/main/tsl/LICENSE-TIMESCALE>

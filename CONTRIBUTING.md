# Contributing to VedettaVip

Thank you for your interest in VedettaVip! Bug reports, ideas and pull requests are welcome. Issues and pull requests
can be written in English or Italian.

## Developer Certificate of Origin (DCO)

Every commit in a pull request must be signed off, certifying that you wrote the change or otherwise have the right to
submit it under the project's license, as stated in the [Developer Certificate of Origin 1.1](https://developercertificate.org/).

Sign off by committing with `-s`:

```bash
git commit -s -m "Fix the label of submap nodes"
```

This adds a line with your real name and email, matching your Git configuration:

```
Signed-off-by: Jane Doe <jane@example.com>
```

To fix commits already made: `git commit --amend -s` (last commit) or `git rebase --signoff main` (whole branch),
then force-push your branch. Pull requests with unsigned commits cannot be merged.

By contributing you agree that your contribution is licensed under the GNU AGPL v3.0 or later, with the additional term
in [NOTICE](NOTICE).

## Development environment

Requirements:

- Linux (other systems should work, but the scripts are written for bash);
- [.NET 10 SDK](https://dotnet.microsoft.com/download);
- Docker Engine with the Compose plugin (for the database);
- the EF Core tool: `dotnet tool install --global dotnet-ef`.

One-time setup, from the repository root:

```bash
# 1. development database (TimescaleDB on 127.0.0.1:5432)
cp deploy/.env.dev.example deploy/.env.dev          # then change POSTGRES_PASSWORD
docker compose -f deploy/docker-compose.dev.yml --env-file deploy/.env.dev up -d --wait

# 2. connection string of the API (user-secrets, never in appsettings)
dotnet user-secrets set "ConnectionStrings:VedettaVip" \
  "Host=127.0.0.1;Port=5432;Database=netmap;Username=netmap;Password=<POSTGRES_PASSWORD>" --project src/VedettaVip.Api

# 3. key shared by the API and the polling agent (generated, never printed)
K=$(openssl rand -base64 32)
dotnet user-secrets set "Agent:ApiKey" "$K" --project src/VedettaVip.Api >/dev/null
dotnet user-secrets set "Agent:ApiKey" "$K" --project src/VedettaVip.Worker >/dev/null
unset K

# 4. database schema and demo data
dotnet ef database update --project src/VedettaVip.Api
```

Run everything (database, build, API, Worker, Web) in one terminal; Ctrl+C stops it all:

```bash
./dev.sh
```

Then open <http://localhost:5032/>: the first time, the API log prints a **setup code** ("Codice di setup") to create
the first administrator.

Local settings that must not be committed go in `deploy/.env.dev`, `deploy/docker-compose.dev.override.yml`,
`appsettings.Local.json` or `appsettings.Development.Local.json` (all ignored by Git).

Notes:

- **ICMP on Linux**: the Worker uses unprivileged ICMP sockets if `net.ipv4.ping_group_range` allows it (the default on
  most distributions), otherwise raw sockets (`CAP_NET_RAW`) or the `ping` executable.
- After `dotnet build` restart `VedettaVip.Web`, and build the solution or `src/VedettaVip.Web`, never the client
  project alone: the WebAssembly asset manifest lives in the host project.
- To simulate a device that is down use the TEST-NET range `192.0.2.0/24` (RFC 5737).

## Tests

```bash
dotnet build
dotnet test
```

Pure logic (state tracking, traffic calculation, alert rules, discovery planner, parsers) is covered by xUnit tests in
`tests/VedettaVip.Tests`; please add tests for new logic.

## Coding guidelines

- C# with nullable enabled, file-scoped namespaces, `record` for DTOs, async everywhere with `CancellationToken`.
- Razor components: markup in `.razor`, logic in the `.razor.cs` code-behind.
- Numbers written into SVG always with `CultureInfo.InvariantCulture`.
- Structured logging with `ILogger<T>`; never log secrets.
- Code comments and UI text are currently in Italian; follow the style of the surrounding code.
- Every new source file (.cs, .razor, .razor.cs, .js, .css, .sh, Dockerfile) starts with the license header, in the
  comment syntax of the language:

  ```csharp
  // SPDX-License-Identifier: AGPL-3.0-or-later
  // Copyright (C) 2026 Marcello Anderlini
  ```

  If you add substantial code you may add your own copyright line below it.
- **No real data**: no real IP addresses, host names, customer names, MAC addresses or credentials in code, tests or
  examples. Use RFC 5737 addresses (`192.0.2.0/24`, `198.51.100.0/24`, `203.0.113.0/24`), `example.com` domains and
  synthetic MAC addresses.
- **New dependencies** must have a license compatible with the AGPLv3 (MIT, BSD, Apache-2.0, PostgreSQL License,
  LGPL…), checked on the actual package (JavaScript bundled inside NuGet packages included), and must be added to
  [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and to the credits of the About page.

## Pull requests

- One topic per pull request, with a short description of the problem and of the solution.
- `dotnet build` without warnings and `dotnet test` green.
- New database changes come with an EF Core migration (`dotnet ef migrations add <Name> --project src/VedettaVip.Api
  --output-dir Data/Migrations`); migrations are never applied automatically at startup.

## Security issues

Do not open public issues for vulnerabilities: see [SECURITY.md](SECURITY.md).

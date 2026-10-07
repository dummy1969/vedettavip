# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 Marcello Anderlini

# VedettaVip.Worker: agente di polling (ICMP, SNMP). Contesto di build: radice del repository.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY VedettaVip.sln Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/VedettaVip.Worker/VedettaVip.Worker.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
# Ping con socket raw da utente non root: CAP_NET_RAW solo sull'eseguibile dotnet. Senza, .NET ripiega
# sul comando "ping", che l'immagine non contiene. Il compose toglie al container tutte le altre capability.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libcap2-bin \
    && setcap cap_net_raw+ep "$(readlink -f /usr/bin/dotnet)" \
    && apt-get purge -y libcap2-bin && apt-get autoremove -y && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "VedettaVip.Worker.dll"]

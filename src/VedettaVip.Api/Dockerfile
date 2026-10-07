# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 Marcello Anderlini

# VedettaVip.Api: API, hub SignalR, notifiche. Contesto di build: radice del repository.
#   target "final"    → immagine dell'API
#   target "migrator" → bundle delle migration EF (eseguito una volta prima dell'API)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY VedettaVip.sln Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/VedettaVip.Api/VedettaVip.Api.csproj -c Release -o /app --no-self-contained

FROM build AS bundle
RUN dotnet tool install --global dotnet-ef --version 10.0.12
ENV PATH="${PATH}:/root/.dotnet/tools"
# Il tool avvia Program.cs fino a Build(): serve una connection string, ma non si collega a nulla
RUN ConnectionStrings__VedettaVip="Host=build;Database=build" \
    dotnet ef migrations bundle --project src/VedettaVip.Api --configuration Release \
        --self-contained --target-runtime linux-x64 --output /bundle/efbundle --verbose

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS migrator
WORKDIR /migrator
COPY --from=bundle /bundle/efbundle .
USER $APP_UID
# La connection string arriva dall'ambiente (ConnectionStrings__VedettaVip), letta da Program.cs
ENTRYPOINT ["./efbundle"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# libgssapi: Npgsql la cerca all'avvio (autenticazione Kerberos/GSS); senza, l'API scrive un errore innocuo nel log
RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "VedettaVip.Api.dll"]

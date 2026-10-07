# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2026 Marcello Anderlini

# VedettaVip.Web: host Blazor (pagine e WebAssembly). Contesto di build: radice del repository.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY VedettaVip.sln Directory.Build.props ./
COPY src/ src/
RUN dotnet publish src/VedettaVip.Web/VedettaVip.Web.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app .
# Chiavi di Data Protection (antiforgery) su volume: senza, a ogni deploy i cookie dei browser non si decifrano più.
# La cartella esiste nell'immagine con il proprietario giusto: il volume nominato ne eredita i permessi al primo montaggio.
RUN mkdir -p /home/app/.aspnet/DataProtection-Keys && chown -R $APP_UID /home/app/.aspnet
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "VedettaVip.Web.dll"]

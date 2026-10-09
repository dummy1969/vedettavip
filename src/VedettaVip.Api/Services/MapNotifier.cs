// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using VedettaVip.Api.Data;
using VedettaVip.Api.Hubs;
using VedettaVip.Shared.Contracts;

namespace VedettaVip.Api.Services;

/// <summary>
/// Avvisa i browser che alcune mappe sono cambiate ("MapsChanged" su /hubs/status): chi le ha aperte le ricarica.
/// Alle mappe indicate si aggiungono le antenate, i cui nodi Submap mostrano stato aggregato e membri delle discendenti.
/// Il messaggio riporta l'header X-VedettaVip-Client della richiesta: la scheda che ha fatto la modifica non ricarica.
/// Una notifica persa non è grave: alla riconnessione dell'hub il client ricarica comunque la mappa.
/// </summary>
public sealed class MapNotifier(IHubContext<StatusHub> hub, IHttpContextAccessor http, ILogger<MapNotifier> logger)
{
    private const int MaxDepth = 64;

    /// <summary>Mappe <paramref name="mapIds"/> cambiate (con le loro antenate). Da chiamare dopo il salvataggio.</summary>
    public async Task MapsChangedAsync(VedettaVipDbContext db, IEnumerable<Guid> mapIds)
    {
        var ids = mapIds.Distinct().ToList();
        if (ids.Count == 0)
            return;

        try
        {
            var parents = await db.Maps.AsNoTracking()
                .Select(m => new { m.Id, m.ParentMapId })
                .ToDictionaryAsync(m => m.Id, m => m.ParentMapId);
            await SendAsync(WithAncestors(ids, parents));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Notifica di modifica delle mappe non inviata");
        }
    }

    /// <summary>Tutte le mappe cambiate (es. una finestra di manutenzione: bordo dei nodi in manutenzione).</summary>
    public async Task AllMapsChangedAsync()
    {
        try
        {
            await SendAsync(null);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Notifica di modifica delle mappe non inviata");
        }
    }

    /// <summary>Mappe in cui compaiono come nodo i device indicati (nome, indirizzo, icona...), con le antenate.</summary>
    public async Task DevicesChangedAsync(VedettaVipDbContext db, IReadOnlyCollection<Guid> deviceIds)
    {
        if (deviceIds.Count == 0)
            return;

        try
        {
            var mapIds = await db.MapNodes.AsNoTracking()
                .Where(n => n.DeviceId != null && deviceIds.Contains(n.DeviceId.Value))
                .Select(n => n.MapId)
                .Distinct()
                .ToListAsync();
            await MapsChangedAsync(db, mapIds);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Notifica di modifica delle mappe dei device non inviata");
        }
    }

    private Task SendAsync(IReadOnlyList<Guid>? mapIds) =>
        // Non legato alla richiesta HTTP: la modifica è già salvata, la notifica deve partire comunque
        hub.Clients.All.SendAsync(StatusHubMessages.MapsChanged, new MapsChangedDto(mapIds, ClientId()), CancellationToken.None);

    /// <summary>Header della scheda del browser, solo se ben formato (finisce in un messaggio inviato a tutti).</summary>
    private string? ClientId()
    {
        var value = http.HttpContext?.Request.Headers[MapClientHeaders.ClientId].ToString();
        return IsValidClientId(value) ? value : null;
    }

    public static bool IsValidClientId(string? value) =>
        value is { Length: > 0 and <= 64 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');

    /// <summary>Le mappe indicate e tutte le loro antenate (protetto da cicli e da profondità eccessive).</summary>
    public static IReadOnlyList<Guid> WithAncestors(IEnumerable<Guid> mapIds, IReadOnlyDictionary<Guid, Guid?> parents)
    {
        var result = new HashSet<Guid>();
        foreach (var id in mapIds)
        {
            Guid? current = id;
            for (var depth = 0; current is { } c && depth <= MaxDepth && result.Add(c); depth++)
                current = parents.GetValueOrDefault(c);
        }
        return [.. result];
    }
}

// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using System.Collections.Concurrent;
using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Worker.State;

/// <summary>Ultimo risultato noto (non Unknown) per dispositivo: è il contenuto dello snapshot periodico.</summary>
public sealed class DeviceStateStore
{
    private readonly ConcurrentDictionary<Guid, DeviceStatusResultDto> latest = new();

    public void Set(DeviceStatusResultDto result)
    {
        if (result.State == NodeState.Unknown)
            latest.TryRemove(result.DeviceId, out _);
        else
            latest[result.DeviceId] = result;
    }

    public void RemoveExcept(IReadOnlySet<Guid> deviceIds)
    {
        foreach (var id in latest.Keys.Where(id => !deviceIds.Contains(id)))
            latest.TryRemove(id, out _);
    }

    public IReadOnlyList<DeviceStatusResultDto> Snapshot() => latest.Values.ToList();

    /// <summary>Ultimo stato noto del dispositivo; null se ancora sconosciuto.</summary>
    public NodeState? StateOf(Guid deviceId) => latest.TryGetValue(deviceId, out var r) ? r.State : null;
}

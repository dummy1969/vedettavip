// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

namespace VedettaVip.Shared.Contracts;

/// <summary>Sorgente da cui è stata letta una serie: campioni grezzi o continuous aggregate.</summary>
public enum MetricSource { Raw, FiveMinutes, OneHour }

/// <summary>Punto di una serie di traffico: media e massimo nel bucket (bps, punto di vista dell'interfaccia). Null = nessun dato.</summary>
public sealed record TrafficPointDto(DateTimeOffset Time, double? InAvg, double? InMax, double? OutAvg, double? OutMax);

/// <summary>Traffico storico di un'interfaccia (GET /api/metrics/interface).</summary>
public sealed record InterfaceSeriesDto(
    Guid DeviceId,
    int IfIndex,
    string? Name,
    long? SpeedBps,
    DateTimeOffset From,
    DateTimeOffset To,
    int BucketSeconds,
    MetricSource Source,
    IReadOnlyList<TrafficPointDto> Points);

/// <summary>Punto di una serie di latenza: RTT medio e massimo dei ping riusciti, perdita in % sui ping inviati.</summary>
public sealed record LatencyPointDto(DateTimeOffset Time, double? RttAvg, double? RttMax, double? LossPct);

/// <summary>Latenza e perdita di un device (GET /api/metrics/device).</summary>
public sealed record DeviceLatencySeriesDto(
    Guid DeviceId,
    DateTimeOffset From,
    DateTimeOffset To,
    int BucketSeconds,
    MetricSource Source,
    IReadOnlyList<LatencyPointDto> Points);

public sealed record RouterOsPointDto(DateTimeOffset Time, double? CpuAvg, double? CpuMax, double? MemoryAvg, double? TemperatureAvg);

/// <summary>CPU, memoria e temperatura di un router RouterOS (GET /api/metrics/device/routeros).</summary>
public sealed record DeviceRouterOsSeriesDto(
    Guid DeviceId,
    DateTimeOffset From,
    DateTimeOffset To,
    int BucketSeconds,
    MetricSource Source,
    IReadOnlyList<RouterOsPointDto> Points);

/// <summary>Interfaccia di un device con storico del traffico (per scegliere quale grafico mostrare).</summary>
public sealed record InterfaceWithHistoryDto(int IfIndex, string? Name, string? Alias, long? SpeedBps);

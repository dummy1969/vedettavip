// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Models;

namespace VedettaVip.Worker.State;

public sealed record StateThresholds(int DownAfterFailures, int UpAfterSuccesses, int SnmpDegradedAfterFailures);

/// <summary>
/// Isteresi dello stato di un dispositivo, senza dipendenze esterne (testabile).
/// Per ogni poll: <see cref="RegisterIcmp"/>; se restituisce true e il device usa SNMP, <see cref="RegisterSnmp"/>.
/// <list type="bullet">
/// <item>Down dopo N ICMP falliti consecutivi; un fallimento isolato non cambia lo stato.</item>
/// <item>Da Down/Unknown torna raggiungibile (Up) dopo M ICMP riusciti consecutivi.</item>
/// <item>Partial dopo K fallimenti SNMP consecutivi con ping ok; torna Up al primo successo SNMP.</item>
/// </list>
/// Le soglie si possono cambiare in corsa (<see cref="Thresholds"/>): i conteggi in corso restano validi.
/// </summary>
public sealed class DeviceStateTracker(StateThresholds thresholds)
{
    /// <summary>Soglie correnti (dalla UI: valori generali o specifici del device).</summary>
    public StateThresholds Thresholds { get; set; } = thresholds;

    private int icmpFailures;
    private int icmpSuccesses;
    private int snmpFailures;

    public NodeState State { get; private set; } = NodeState.Unknown;

    private bool Reachable => State is NodeState.Up or NodeState.Partial;

    /// <summary>Registra l'esito ICMP. Restituisce true se in questo poll va interrogato SNMP.</summary>
    public bool RegisterIcmp(bool success)
    {
        if (success)
        {
            icmpFailures = 0;
            icmpSuccesses++;
            if (!Reachable && icmpSuccesses >= Thresholds.UpAfterSuccesses)
            {
                State = NodeState.Up;
                snmpFailures = 0;
            }
            return Reachable;
        }

        icmpSuccesses = 0;
        icmpFailures++;
        if (icmpFailures >= Thresholds.DownAfterFailures)
        {
            State = NodeState.Down;
            snmpFailures = 0;
        }
        // Ping fallito: SNMP non si interroga in questo poll (lo stato resta quello precedente)
        return false;
    }

    public void RegisterSnmp(bool success)
    {
        if (!Reachable)
            return;

        if (success)
        {
            snmpFailures = 0;
            State = NodeState.Up;
            return;
        }

        snmpFailures++;
        if (snmpFailures >= Thresholds.SnmpDegradedAfterFailures)
            State = NodeState.Partial;
    }
}

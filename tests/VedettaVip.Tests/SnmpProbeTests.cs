// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Worker.Probes;

namespace VedettaVip.Tests;

public class SnmpProbeTests
{
    /// <summary>
    /// Regressione: con request-id negativi RouterOS risponde con un intero BER a 5 byte che SharpSnmpLib
    /// rifiuta, e il device resta Partial anche con SNMP funzionante.
    /// </summary>
    [Fact]
    public void Request_ids_are_always_positive()
    {
        for (var i = 0; i < 100_000; i++)
            Assert.InRange(SnmpProbe.NextRequestId(), 1, int.MaxValue);
    }
}

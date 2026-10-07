// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;
using VedettaVip.Worker.Probes;
using VedettaVip.Worker.State;

namespace VedettaVip.Tests;

public class TrafficCalculatorTests
{
    private static readonly Guid Device = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    /// <param name="uptime">sysUpTime in centesimi di secondo.</param>
    private static SnmpInterfaceCounters C(uint uptime, ulong inOctets, ulong outOctets, int ifIndex = 2) =>
        new(ifIndex, uptime, inOctets, outOctets, 1_000_000_000, "ether2");

    [Fact]
    public void First_sample_is_only_a_baseline()
    {
        var calc = new TrafficCalculator();
        Assert.Null(calc.Add(Device, C(10_000, 1_000, 2_000), Now));
    }

    [Fact]
    public void Computes_bits_per_second_from_device_uptime()
    {
        var calc = new TrafficCalculator();
        calc.Add(Device, C(10_000, 1_000_000, 5_000_000), Now);

        // 30 s dopo (3000 tick): +3.750.000 byte in = 1 Mbps, +37.500.000 byte out = 10 Mbps
        var s = calc.Add(Device, C(13_000, 4_750_000, 42_500_000), Now);

        Assert.NotNull(s);
        Assert.Equal((1_000_000L, 10_000_000L), (s.InBps, s.OutBps));
        Assert.Equal((Device, 2, 1_000_000_000L, "ether2"), (s.DeviceId, s.IfIndex, s.SpeedBps!.Value, s.Name));
    }

    [Fact]
    public void Counter_reset_discards_the_sample_and_restarts_from_the_new_baseline()
    {
        var calc = new TrafficCalculator();
        calc.Add(Device, C(10_000, 9_000_000, 9_000_000), Now);

        Assert.Null(calc.Add(Device, C(13_000, 100, 9_100_000), Now)); // contatore in diminuito

        var s = calc.Add(Device, C(16_000, 3_750_100, 9_100_000), Now);
        Assert.Equal((1_000_000L, 0L), (s!.InBps, s.OutBps));
    }

    [Fact]
    public void Device_reboot_discards_the_sample()
    {
        var calc = new TrafficCalculator();
        calc.Add(Device, C(900_000, 1_000_000, 1_000_000), Now);

        Assert.Null(calc.Add(Device, C(3_000, 2_000_000, 2_000_000), Now)); // sysUpTime ripartito
        Assert.NotNull(calc.Add(Device, C(6_000, 3_000_000, 3_000_000), Now));
    }

    [Fact]
    public void Samples_closer_than_one_second_keep_the_previous_baseline()
    {
        var calc = new TrafficCalculator();
        calc.Add(Device, C(10_000, 0, 0), Now);

        Assert.Null(calc.Add(Device, C(10_050, 1_000, 1_000), Now)); // 0,5 s: ignorato

        // Il delta successivo parte ancora dalla base a 10.000 tick: 10 s, 1.250.000 byte = 1 Mbps
        var s = calc.Add(Device, C(11_000, 1_250_000, 0), Now);
        Assert.Equal(1_000_000L, s!.InBps);
    }

    [Fact]
    public void Reset_and_RetainOnly_forget_the_baselines()
    {
        var calc = new TrafficCalculator();
        calc.Add(Device, C(10_000, 0, 0, ifIndex: 1), Now);
        calc.Add(Device, C(10_000, 0, 0, ifIndex: 2), Now);

        // Resta misurata solo l'interfaccia 2
        calc.RetainOnly([new AgentTargetDto(Device, "r", "10.0.0.1", SnmpVersion.V2c, [2])]);
        Assert.Null(calc.Add(Device, C(13_000, 1, 1, ifIndex: 1), Now));
        Assert.NotNull(calc.Add(Device, C(13_000, 1, 1, ifIndex: 2), Now));

        calc.Reset(Device);
        Assert.Null(calc.Add(Device, C(16_000, 2, 2, ifIndex: 2), Now));
    }
}

/// <summary>Contatori a 32 bit (apparati senza ifXTable, es. DrayTek Vigor): giro del contatore a 2³² byte.</summary>
public class TrafficCalculator32BitTests
{
    private static readonly Guid Device = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);
    private const ulong Max32 = uint.MaxValue;

    private static SnmpInterfaceCounters C(uint uptime, ulong inOctets, ulong outOctets, long speed = 100_000_000, bool is32 = true) =>
        new(4, uptime, inOctets, outOctets, speed, "WAN1", is32);

    [Fact]
    public void Wrap_of_a_32_bit_counter_is_corrected()
    {
        var calc = new TrafficCalculator();
        calc.Add(Device, C(10_000, Max32 - 999_999, 1_000), Now);
        // 30 s dopo: il contatore in è ripartito da zero, 1.000.000 + 2.750.000 byte = 1 Mbit/s
        var s = calc.Add(Device, C(13_000, 2_750_000, 3_751_000), Now);
        Assert.NotNull(s);
        Assert.Equal(1_000_000, s.InBps);
        Assert.Equal(1_000_000, s.OutBps);
    }

    [Fact]
    public void Implausible_wrap_is_treated_as_reset()
    {
        var calc = new TrafficCalculator();
        calc.Add(Device, C(10_000, 3_000_000_000, 1_000), Now);
        // Calo che, letto come giro, varrebbe ~370 Mbit/s su un'interfaccia da 100: è un reset, si riparte
        Assert.Null(calc.Add(Device, C(13_000, 100_000_000, 2_000), Now));
        Assert.NotNull(calc.Add(Device, C(16_000, 100_375_000, 3_000), Now)); // la base è ripartita
    }

    [Fact]
    public void A_64_bit_counter_going_down_is_still_a_reset()
    {
        var calc = new TrafficCalculator();
        calc.Add(Device, C(10_000, Max32 - 1000, 1_000, is32: false), Now);
        Assert.Null(calc.Add(Device, C(13_000, 500, 2_000, is32: false), Now));
    }

    [Fact]
    public void Switching_between_32_and_64_bit_restarts_the_baseline()
    {
        var calc = new TrafficCalculator();
        calc.Add(Device, C(10_000, 5_000_000, 1_000, is32: true), Now);
        Assert.Null(calc.Add(Device, C(13_000, 9_000_000_000, 2_000, is32: false), Now));
    }
}

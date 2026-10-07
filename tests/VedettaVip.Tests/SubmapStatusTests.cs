// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 Marcello Anderlini

using VedettaVip.Shared.Contracts;
using VedettaVip.Shared.Models;

namespace VedettaVip.Tests;

public class SubmapStatusTests
{
    private static SubmapMemberDto M(NodeState s, bool maintenance = false) => new(Guid.NewGuid(), s, maintenance);

    private static NodeState Aggregate(params SubmapMemberDto[] members) => SubmapStatus.Aggregate(SubmapStatus.Count(members));

    [Fact]
    public void Worst_state_wins() =>
        Assert.Equal(NodeState.Down, Aggregate(M(NodeState.Up), M(NodeState.Partial), M(NodeState.Down), M(NodeState.Unknown)));

    [Fact]
    public void Partial_beats_up() => Assert.Equal(NodeState.Partial, Aggregate(M(NodeState.Up), M(NodeState.Partial)));

    [Fact]
    public void Unknown_devices_do_not_grey_out_a_healthy_submap() =>
        Assert.Equal(NodeState.Up, Aggregate(M(NodeState.Up), M(NodeState.Unknown)));

    [Fact]
    public void Unknown_only_when_nothing_is_known()
    {
        Assert.Equal(NodeState.Unknown, Aggregate(M(NodeState.Unknown), M(NodeState.Unknown)));
        Assert.Equal(NodeState.Unknown, Aggregate());
    }

    [Fact]
    public void Devices_in_maintenance_do_not_color_the_submap_but_are_counted()
    {
        var counts = SubmapStatus.Count([M(NodeState.Up), M(NodeState.Down, maintenance: true)]);
        Assert.Equal(NodeState.Up, SubmapStatus.Aggregate(counts));
        Assert.Equal((1, 0, 1, 2), (counts.Up, counts.Down + counts.Partial == 0 ? 0 : 1, counts.Maintenance, counts.Total));
    }

    [Theory]
    [InlineData(10, 1, 2, 0, "2 down · 1 partial su 13")]
    [InlineData(12, 0, 0, 0, "12 up")]
    [InlineData(3, 0, 0, 2, "3 up · 2 sconosciuti")]
    [InlineData(0, 0, 0, 4, "4 sconosciuti")]
    [InlineData(0, 0, 0, 0, "nessun dispositivo")]
    public void Summary_text(int up, int partial, int down, int unknown, string expected) =>
        Assert.Equal(expected, new SubmapCounts(up, partial, down, unknown, 0).Summary());

    [Fact]
    public void Live_update_of_a_member_recomputes_state_and_label_values()
    {
        var device = Guid.NewGuid();
        var node = new MapNode { Kind = MapNodeKind.Submap };
        node.SubmapMembers[device] = new SubmapMemberDto(device, NodeState.Up, false);
        node.RefreshSubmapState();
        Assert.Equal((NodeState.Up, "1 up"), (node.State, node.Values["Summary"]));

        Assert.True(node.UpdateSubmapMember(device, NodeState.Down));
        Assert.Equal((NodeState.Down, "1 down su 1", "1"), (node.State, node.Values["Summary"], node.Values["Down"]));
        Assert.False(node.UpdateSubmapMember(Guid.NewGuid(), NodeState.Down)); // device non contenuto
    }
}

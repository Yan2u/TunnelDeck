using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class SidebarProjectionTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(10)]
    public void PinnedComesFirstAndRecentFillsEachSectionIndependently(int maximum)
    {
        ImmutableArray<RemoteServer> servers = Enumerable.Range(1, 12).Select(id => DomainData.Server(id, lastUsed: DomainData.Now.AddMinutes(id))).ToImmutableArray();
        ImmutableArray<ForwardProfile> profiles = Enumerable.Range(1, 12).Select(id => DomainData.Profile(id) with { LastUsedAt = DomainData.Now.AddMinutes(id) }).ToImmutableArray();
        AppState state = DomainData.State(servers, profiles) with { Settings = new AppSettings { SidebarMaximumItems = maximum } };
        state = state with
        {
            Order = ResourceOrderService.Pin(ResourceOrderService.Pin(state.Order, ResourceKind.Server, servers[0].Id), ResourceKind.Profile, profiles[0].Id),
        };
        SidebarItems projection = SidebarProjection.Project(state);
        Assert.Equal(maximum, projection.ServerIds.Length);
        Assert.Equal(maximum, projection.ProfileIds.Length);
        Assert.Equal(servers[0].Id, projection.ServerIds[0]);
        Assert.Equal(profiles[0].Id, projection.ProfileIds[0]);
        Assert.Equal(servers.Reverse().Take(maximum - 1).Select(server => server.Id), projection.ServerIds.Skip(1));
        Assert.Equal(profiles.Reverse().Take(maximum - 1).Select(profile => profile.Id), projection.ProfileIds.Skip(1));
        Assert.Equal(maximum, projection.ServerIds.Distinct().Count());
    }

    [Fact]
    public void ExcessPinnedItemsUsePinnedOrderWithoutRecentOverflow()
    {
        RecentResource[] resources = Enumerable.Range(1, 12).Select(id => new RecentResource(DomainData.Id(id), DomainData.Now)).ToArray();
        Guid[] pinned = resources.Reverse().Select(item => item.Id).ToArray();
        Assert.Equal(pinned.Take(5), SidebarProjection.Select(resources, pinned, 5));
    }

    [Fact]
    public void EmptyNeverUsedStaleAndDuplicatePinnedItemsAreHandled()
    {
        Assert.Empty(SidebarProjection.Project(new AppState()).ProfileIds);
        RecentResource[] resources = [new(DomainData.Id(1), null), new(DomainData.Id(2), null)];
        Assert.Empty(SidebarProjection.Select(resources, [], 5));
        Assert.Equal(new[] { DomainData.Id(1) }, SidebarProjection.Select(resources, [DomainData.Id(99), DomainData.Id(1), DomainData.Id(1)], 5));
    }

    [Fact]
    public void EqualTimestampsHaveAStableIdTieBreakAcrossInputOrder()
    {
        RecentResource[] resources = Enumerable.Range(1, 6).Select(id => new RecentResource(DomainData.Id(id), DomainData.Now)).ToArray();
        Assert.Equal(resources.Take(3).Select(item => item.Id), SidebarProjection.Select(resources.Reverse(), [], 3));
        Assert.Equal(SidebarProjection.Select(resources, [], 5).ToArray(), SidebarProjection.Select(resources.Reverse(), [], 5).ToArray());
    }

    [Fact]
    public void UsingAnOtherResourceChangesProjectionWithoutReorderingOther()
    {
        RemoteServer[] servers = Enumerable.Range(1, 6).Select(id => DomainData.Server(id, lastUsed: DomainData.Now.AddMinutes(id))).ToArray();
        AppState before = DomainData.State([.. servers], []);
        Guid[] order = before.Order.OtherServerIds.ToArray();
        AppState after = before with { Servers = before.Servers.SetItem(0, servers[0] with { LastUsedAt = DomainData.Now.AddDays(1) }) };
        Assert.Equal(servers[0].Id, SidebarProjection.Project(after).ServerIds[0]);
        Assert.Equal(order, after.Order.OtherServerIds.ToArray());
        Assert.Equal(order, before.Order.OtherServerIds.ToArray());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(11)]
    public void SettingsLimitsAreEnforced(int maximum) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => SidebarProjection.Select([], [], maximum));
}

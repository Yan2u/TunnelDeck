using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Tunnels;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Tests;

public sealed class DeletionImpactTests
{
    [Fact]
    public void SingleAndMultipleServersDeduplicateProfilesAndIgnoreDuplicateDisplayNames()
    {
        RemoteServer first = DomainData.Server() with { DisplayName = "same" }, second = DomainData.Server(2) with { DisplayName = "same" };
        var shared = DomainData.Profile(1, DomainData.Rule(1, 1), DomainData.Rule(2, 2));
        var other = DomainData.Profile(2, DomainData.Rule(3, 2)) with { Name = shared.Name };
        AppState state = DomainData.State([first, second, DomainData.Server(3)], [shared, other, DomainData.Profile(3)]);
        var single = Calculate(state, [first.Id]);
        Assert.Single(single.Servers); Assert.Single(single.Profiles); Assert.Equal(1, single.RuleCount);
        var multiple = Calculate(state, [first.Id, second.Id, second.Id]);
        Assert.Equal(2, multiple.Servers.Length); Assert.Equal(2, multiple.ProfileCount); Assert.Equal(3, multiple.RuleCount);
        var all = DeletionImpactCalculator.Calculate(state, new(0, []), new(0, []), new(ResourceKind.Server, [], true));
        Assert.Equal(3, all.ResourceCount); Assert.Equal(2, all.ProfileCount); Assert.Equal(3, all.RuleCount);
    }

    [Fact]
    public void EmptyAndLargeImpactsKeepPreciseCounts()
    {
        AppState state = DomainData.State([DomainData.Server()], [DomainData.Profile(1)]);
        var empty = Calculate(state, [DomainData.Id(1)]);
        Assert.Equal(0, empty.RuleCount); Assert.Empty(empty.Profiles); Assert.Empty(empty.Tunnels);
        state = state with { Profiles = [DomainData.Profile(1, Enumerable.Range(1, 1000).Select(id => DomainData.Rule(id, 1)).ToArray())] };
        var large = Calculate(state, [DomainData.Id(1)]);
        Assert.Equal(1000, large.RuleCount); Assert.Single(large.Profiles);
    }

    [Fact]
    public void StartingReconnectingAndPendingCredentialsAreIncludedWithoutCountingBoundWorkTwice()
    {
        AppState state = DomainData.State([DomainData.Server()], []);
        var running = new ActiveTunnel { Id = Guid.NewGuid(), Requested = DomainData.Rule(), State = TunnelState.Running };
        var starting = running with { Id = Guid.NewGuid(), State = TunnelState.Starting };
        var reconnecting = running with { Id = Guid.NewGuid(), State = TunnelState.Reconnecting };
        var stopped = running with { Id = Guid.NewGuid(), State = TunnelState.Stopped };
        var launch = new TunnelDeck.Core.Snapshots.RuleLaunchSnapshot(state.Servers[0], DomainData.Rule(), new());
        var work = new ServerWorkSnapshot(Guid.NewGuid(), state.Servers[0], ServerWorkKind.Profile,
            [new(launch, starting.Id), new(launch with { Requested = DomainData.Rule(2) })]);
        var impact = DeletionImpactCalculator.Calculate(state, new(17, [running, starting, reconnecting, stopped]),
            new(23, [work]), new(ResourceKind.Server, [state.Servers[0].Id]));
        Assert.Equal(3, impact.Tunnels.Length); Assert.Equal(1, impact.PendingForwardCount);
        Assert.Equal(17, impact.RuntimeRevision); Assert.Equal(23, impact.WorkRevision);
    }

    [Fact]
    public void ServerRemovalClearsReferencesKeepsEmptyProfilesAndPreservesHistory()
    {
        RemoteServer server = DomainData.Server();
        AppState state = DomainData.State([server, DomainData.Server(2)], [DomainData.Profile(1, DomainData.Rule())]);
        state = state with { Settings = state.Settings with { DefaultServerId = server.Id, PinnedQuickServerId = server.Id } };
        var removed = DeletionImpactCalculator.Remove(state, Calculate(state, [server.Id]));
        Assert.Single(removed.Servers); Assert.Single(removed.Profiles); Assert.Empty(removed.Profiles[0].Rules);
        Assert.Null(removed.Settings.DefaultServerId); Assert.Null(removed.Settings.PinnedQuickServerId);
        Assert.DoesNotContain(server.Id, removed.Order.PinnedServerIds); Assert.DoesNotContain(server.Id, removed.Order.OtherServerIds);
        Assert.Equal(state.RecentActivity, removed.RecentActivity); Assert.Equal(state.Statistics, removed.Statistics);
        AppStateValidator.Validate(removed).ThrowIfInvalid();
    }

    [Fact]
    public void ConfirmationIgnoresUsageRevisionsButDetectsChangedResourcesAndRuntime()
    {
        AppState state = DomainData.State([DomainData.Server()], [DomainData.Profile(1, DomainData.Rule())]);
        var before = Calculate(state, [DomainData.Id(1)]);
        var activityOnly = state with { Revision = 99, Servers = [state.Servers[0] with { LastUsedAt = DateTimeOffset.UtcNow }] };
        Assert.True(DeletionImpactCalculator.SameEffect(before, Calculate(activityOnly, [DomainData.Id(1)])));
        var changed = state with { Profiles = [state.Profiles[0] with { Rules = state.Profiles[0].Rules.Add(DomainData.Rule(2)) }] };
        Assert.False(DeletionImpactCalculator.SameEffect(before, Calculate(changed, [DomainData.Id(1)])));
        var runtime = new TunnelManagerSnapshot(1, [new() { Id = Guid.NewGuid(), Requested = DomainData.Rule(), State = TunnelState.Running }]);
        var after = DeletionImpactCalculator.Calculate(state, runtime, new(0, []), before.Request);
        Assert.False(DeletionImpactCalculator.SameEffect(before, after));
    }

    private static DeletionImpact Calculate(AppState state, Guid[] ids) =>
        DeletionImpactCalculator.Calculate(state, new(0, []), new(0, []), new(ResourceKind.Server, [.. ids.Distinct()]));
}

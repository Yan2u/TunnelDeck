using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Profiles;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Tests;

public sealed class ProfileRunnerTests
{
    [Fact]
    public async Task CrossServerPartialSuccessAndRetryOnlyFailedRulesKeepOtherTunnelsRunning()
    {
        ForwardRule[] rules = [DomainData.Rule(1), DomainData.Rule(2, localPort: 4000), DomainData.Rule(3, 2, ForwardMode.Remote)];
        await using var fixture = await ProfileFixture.CreateAsync(rules: rules);
        bool refuse = true;
        fixture.Backend.Start = (session, rule, _) => refuse && rule.ServerId == DomainData.Id(2)
            ? Task.FromException<IForwardHandle>(new ForwardException(new(TunnelFailureKind.RemoteListenFailure, "Remote listen rejected.")))
            : Task.FromResult<IForwardHandle>(fixture.Backend.CreateHandle(session, rule));
        ResourceOrder order = fixture.Backend.Connections.Configuration.Current!.Order;
        ProfileRunSnapshot first = await fixture.Runner.StartAllAsync(fixture.Profile.Id);
        Assert.Equal(2, first.StartedCount); Assert.Equal(1, first.FailedCount);
        Assert.Equal(2, fixture.Backend.Manager.Current.RunningCount);
        Guid[] survivors = fixture.Backend.Manager.Current.Tunnels.Where(tunnel => tunnel.IsRunning).Select(tunnel => tunnel.Id).ToArray();
        refuse = false;
        ProfileRunSnapshot retried = await fixture.Runner.RetryFailedAsync(fixture.Profile.Id);
        Assert.Equal(0, retried.FailedCount); Assert.Equal(3, retried.StartedCount);
        Assert.Equal(3, fixture.Backend.Manager.Current.RunningCount);
        Assert.All(survivors, id => Assert.Contains(fixture.Backend.Manager.Current.Tunnels, tunnel => tunnel.Id == id && tunnel.IsRunning));
        Assert.Equal(1, fixture.Backend.Attempts.Count(rule => rule.Id == rules[0].Id));
        Assert.Equal(1, fixture.Backend.Attempts.Count(rule => rule.Id == rules[1].Id));
        Assert.Equal(2, fixture.Backend.Attempts.Count(rule => rule.Id == rules[2].Id));
        Assert.True(await fixture.Activity.FlushAsync());
        AppState state = fixture.Backend.Connections.Configuration.Current!;
        Assert.Equal(3, state.Statistics.TotalForwards);
        Assert.Equal(2, state.Statistics.ProfileUses[fixture.Profile.Id]);
        Assert.Equal(order, state.Order);
        Assert.Contains(fixture.Profile.Id, SidebarProjection.Project(state).ProfileIds);
    }

    [Fact]
    public async Task DoubleStartSharesOneOperationAndLaterDuplicatesDoNotIncreaseUsage()
    {
        await using var fixture = await ProfileFixture.CreateAsync(rules: [DomainData.Rule()]);
        using var gate = new AsyncGate();
        fixture.Backend.Start = async (session, rule, token) => { await gate.WaitAsync(token); return fixture.Backend.CreateHandle(session, rule); };
        Task<ProfileRunSnapshot> first = fixture.Runner.StartAllAsync(fixture.Profile.Id);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<ProfileRunSnapshot> duplicate = fixture.Runner.StartAllAsync(fixture.Profile.Id);
        Assert.Same(first, duplicate);
        gate.Release.TrySetResult();
        Assert.Equal(1, (await first).StartedCount);
        var repeated = await fixture.Runner.StartAllAsync(fixture.Profile.Id);
        Assert.Equal(1, repeated.AlreadyRunningCount); Assert.Equal(0, repeated.StartedCount);
        Assert.Single(fixture.Backend.Attempts);
        Assert.True(await fixture.Activity.FlushAsync());
        var statistics = fixture.Backend.Connections.Configuration.Current!.Statistics;
        Assert.Equal(1, statistics.TotalForwards); Assert.Equal(1, statistics.ProfileUses[fixture.Profile.Id]);
    }

    [Fact]
    public async Task OneTransientCredentialPromptCanServeSeveralRemoteTransportsInOneInvocation()
    {
        await using var fixture = await ProfileFixture.CreateAsync(rules:
            [DomainData.Rule(1, mode: ForwardMode.Remote), DomainData.Rule(2, mode: ForwardMode.Remote, remotePort: 5433)]);
        ProfileRunSnapshot result = await fixture.Runner.StartAllAsync(fixture.Profile.Id);
        Assert.Equal(2, result.StartedCount);
        Assert.Single(fixture.Prompts);
        Assert.Equal(2, fixture.Backend.Connections.Factory.Sessions.Count);
        Assert.True(await fixture.Activity.FlushAsync());
        Assert.Null(fixture.Backend.Connections.Configuration.Current!.Servers[0].CredentialReference);
        Assert.Empty(fixture.Backend.Connections.Credentials.Values);
    }

    [Fact]
    public async Task StartingOneRuleDoesNotStartItsSiblings()
    {
        ForwardRule[] rules = [DomainData.Rule(), DomainData.Rule(2, 2, localPort: 4000)];
        await using var fixture = await ProfileFixture.CreateAsync(rules: rules);
        ProfileRunSnapshot result = await fixture.Runner.StartRuleAsync(fixture.Profile.Id, rules[1].Id);
        Assert.Equal(rules[1], Assert.Single(fixture.Backend.Attempts));
        Assert.Equal(ProfileRuleOutcome.Pending, result.Rules[0].Outcome);
        Assert.Equal(ProfileRuleOutcome.Started, result.Rules[1].Outcome);
        Assert.Equal(fixture.Profile.Id, fixture.Backend.Manager.Current.Tunnels.Single().Source.ProfileId);
    }

    [Fact]
    public async Task SavedEditsDuringAStartNeverAlterItsRequestedSnapshotOrSourceName()
    {
        await using var fixture = await ProfileFixture.CreateAsync(rules: [DomainData.Rule()]);
        using var gate = new AsyncGate();
        fixture.Backend.Start = async (session, rule, token) => { await gate.WaitAsync(token); return fixture.Backend.CreateHandle(session, rule); };
        var work = fixture.Runner.StartAllAsync(fixture.Profile.Id);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var changed = fixture.Profile with { Name = "Renamed after starting", Rules = [fixture.Profile.Rules[0] with { LocalPort = 6000 }] };
        await fixture.Backend.Connections.Configuration.SaveProfileAsync(changed, fixture.Profile);
        gate.Release.TrySetResult(); await work;
        ActiveTunnel tunnel = fixture.Backend.Manager.Current.Tunnels.Single();
        Assert.Equal(3000, tunnel.Requested.LocalPort);
        Assert.Equal(fixture.Profile.Name, tunnel.Source.ProfileName);
        Assert.True(await fixture.Activity.FlushAsync());
        ForwardProfile saved = fixture.Backend.Connections.Configuration.Current!.Profiles.Single();
        Assert.Equal(6000, saved.Rules[0].LocalPort); Assert.Equal(changed.Name, saved.Name); Assert.NotNull(saved.LastUsedAt);
    }

    [Fact]
    public async Task FailedUsageCommitsCanBeRetriedWithoutDoubleCountingTheProfile()
    {
        await using var fixture = await ProfileFixture.CreateAsync(rules: [DomainData.Rule()]);
        fixture.Backend.Connections.Store.BeforeCommit = () => Task.FromException(new IOException("Synthetic activity failure."));
        var result = await fixture.Runner.StartAllAsync(fixture.Profile.Id);
        Assert.Equal(1, result.StartedCount);
        Assert.False(await fixture.Activity.FlushAsync());
        Assert.Equal(0, fixture.Backend.Connections.Configuration.Current!.Statistics.TotalForwards);
        fixture.Backend.Connections.Store.BeforeCommit = null;
        Assert.True(await fixture.Activity.FlushAsync());
        fixture.Activity.RecordProfileUse(result.OperationId, fixture.Profile.Id, DomainData.Now);
        Assert.True(await fixture.Activity.FlushAsync());
        var statistics = fixture.Backend.Connections.Configuration.Current!.Statistics;
        Assert.Equal(1, statistics.TotalForwards); Assert.Equal(1, statistics.ProfileUses[fixture.Profile.Id]);
    }

    [Fact]
    public async Task PreviewAndEmptyProfilesNeverStartNetworkWorkOrUsage()
    {
        await using var preview = await ProfileFixture.CreateAsync(false, DomainData.Rule());
        await Assert.ThrowsAsync<InvalidOperationException>(() => preview.Runner.StartAllAsync(preview.Profile.Id));
        Assert.Empty(preview.Backend.Attempts); Assert.Empty(preview.Prompts);
        await using var empty = await ProfileFixture.CreateAsync();
        Assert.Empty((await empty.Runner.StartAllAsync(empty.Profile.Id)).Rules);
        Assert.Empty(empty.Backend.Attempts);
        Assert.Empty(empty.Backend.Connections.Configuration.Current!.Statistics.ProfileUses);
    }
}

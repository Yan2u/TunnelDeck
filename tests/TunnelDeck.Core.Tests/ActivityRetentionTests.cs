using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Tests;

public sealed class ActivityRetentionTests
{
    [Fact]
    public async Task LateEventsDoNotEvictNewerHistoryAndCountingSurvivesTrimming()
    {
        var clock = new TimerTimeProvider();
        await using var fixture = await TunnelFixture.CreateAsync(clock);
        ImmutableArray<ActivityEntry> seed = [.. Enumerable.Range(1, AppState.MaximumRecentEntries).Select(index => new ActivityEntry
        {
            Id = Guid.NewGuid(), OccurredAt = clock.GetUtcNow().AddSeconds(index), Outcome = ActivityOutcome.Started,
            Requested = DomainData.Rule(), Source = new() { ServerName = "Previous session" },
        }).Reverse()];
        await fixture.Connections.Configuration.UpdateAsync(state => state with
        {
            RecentActivity = seed, Statistics = new() { TotalForwards = 200, ServerForwards = ImmutableDictionary<Guid, long>.Empty.Add(DomainData.Id(1), 200) },
        });
        using var recorder = new TunnelActivityRecorder(fixture.Manager, fixture.Connections.Configuration);
        var started = await fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        Assert.True(await recorder.FlushAsync());
        AppState state = fixture.Connections.Configuration.Current!;
        Assert.Equal(seed.ToArray(), state.RecentActivity.ToArray());
        Assert.Equal(201, state.Statistics.TotalForwards);
        Assert.Equal(201, state.Statistics.ServerForwards[DomainData.Id(1)]);
        await fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        Assert.True(await recorder.FlushAsync());
        Assert.Equal(201, fixture.Connections.Configuration.Current!.Statistics.TotalForwards);
        clock.Advance(TimeSpan.FromSeconds(201));
        await fixture.Manager.StopAsync(started.Tunnel.Id);
        Assert.True(await recorder.FlushAsync());
        state = fixture.Connections.Configuration.Current!;
        Assert.Equal(200, state.RecentActivity.Length);
        Assert.Equal(ActivityOutcome.Stopped, state.RecentActivity[0].Outcome);
        Assert.DoesNotContain(seed[^1], state.RecentActivity);
        Assert.Equal(201, state.Statistics.TotalForwards);
        await fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        Assert.True(await recorder.FlushAsync());
        Assert.Equal(202, fixture.Connections.Configuration.Current!.Statistics.TotalForwards);
    }

    [Fact]
    public void ANewQuickDraftUsesChangedDefaultsWhileAnEditedDraftKeepsItsSettings()
    {
        AppState state = DomainData.State([DomainData.Server(), DomainData.Server(2)], []) with
        { Settings = new() { DefaultServerId = DomainData.Id(1), AutoFindAvailablePortDefault = true } };
        var edited = new QuickForwardModel();
        edited.Reconcile(state, false, true);
        edited.PortText = "3000";
        edited.UseAdvanced = true;
        edited.RemoteHost = "localhost";
        edited.RemotePortText = "5432";
        state = state with { Settings = state.Settings with { DefaultServerId = DomainData.Id(2), AutoFindAvailablePortDefault = false } };
        edited.Reconcile(state, false, true);
        Assert.Equal(DomainData.Id(1), edited.SelectedServerId);
        Assert.True(edited.AutoFindPort);
        Assert.Equal("localhost", edited.Capture()!.Requested.RemoteHost);
        var fresh = new QuickForwardModel();
        fresh.Reconcile(state, false, true);
        fresh.PortText = "4000";
        Assert.Equal(DomainData.Id(2), fresh.SelectedServerId);
        Assert.False(fresh.Capture()!.Requested.AutoFindAvailablePort);
        fresh.ModeIndex = 1;
        fresh.AutoFindPort = true;
        Assert.False(fresh.Capture()!.Requested.AutoFindAvailablePort);
    }
}

using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class DeletionTransactionTests
{
    [Fact]
    public async Task ReviewingOrCancellingAConfirmationDoesNotStopOrWriteAnything()
    {
        await using var fixture = await DeletionFixture.CreateAsync(DomainData.Rule());
        await fixture.Profiles.StartAllAsync(DomainData.Profile(1).Id);
        await fixture.Activity.FlushAsync();
        AppState before = fixture.Configuration.Current!;
        var impact = fixture.Deletion.CalcServerDeletionImpact([DomainData.Id(1)]);
        Assert.Single(impact.Tunnels); Assert.Same(before, fixture.Configuration.Current);
        Assert.All(fixture.Backend.Handles, handle => Assert.Equal(0, handle.StopCalls));
    }

    [Fact]
    public async Task NewRulesDuringConfirmationRequireReviewBeforeStopping()
    {
        await using var fixture = await DeletionFixture.CreateAsync(DomainData.Rule());
        await fixture.Profiles.StartAllAsync(DomainData.Profile(1).Id);
        await fixture.Activity.FlushAsync();
        var confirmed = fixture.Deletion.CalcServerDeletionImpact([DomainData.Id(1)]);
        ForwardProfile original = fixture.Configuration.Current!.Profiles[0];
        await fixture.Configuration.SaveProfileAsync(original with { Rules = original.Rules.Add(DomainData.Rule(2)) }, original);
        var result = await fixture.Deletion.DeleteConfirmedAsync(confirmed);
        Assert.Equal(DeletionOutcome.NeedsConfirmation, result.Outcome);
        Assert.Equal(2, result.Impact.RuleCount);
        Assert.All(fixture.Backend.Handles, handle => Assert.True(handle.IsStarted));
        Assert.True((await fixture.Deletion.DeleteConfirmedAsync(result.Impact)).Succeeded);
        Assert.Empty(fixture.Configuration.Current!.Profiles[0].Rules);
    }

    [Fact]
    public async Task NewlyStartedTunnelAndNewBulkResourceRequireFreshConfirmation()
    {
        await using var fixture = await DeletionFixture.CreateAsync();
        var confirmed = fixture.Deletion.CalcAllDeletionImpact(ResourceKind.Server);
        var started = await fixture.Backend.Manager.StartAsync(fixture.Backend.Launch(), ConnectionFixture.Input);
        await fixture.Activity.FlushAsync();
        var runtimeChanged = await fixture.Deletion.DeleteConfirmedAsync(confirmed);
        Assert.Equal(DeletionOutcome.NeedsConfirmation, runtimeChanged.Outcome);
        Assert.Equal(TunnelState.Running, fixture.Backend.Manager.Current.Tunnels.Single().State);
        var refreshed = fixture.Deletion.CalcAllDeletionImpact(ResourceKind.Server);
        await fixture.Backend.Connections.Manager.SaveServerAsync(DomainData.Server(3), TunnelDeck.Core.Persistence.CredentialChange.Forget);
        var resourcesChanged = await fixture.Deletion.DeleteConfirmedAsync(refreshed);
        Assert.Equal(DeletionOutcome.NeedsConfirmation, resourcesChanged.Outcome); Assert.Equal(3, resourcesChanged.Impact.ResourceCount);
        Assert.Equal(0, fixture.Backend.Handles.Single().StopCalls);
    }

    [Fact]
    public async Task SecondCleanupFailureKeepsEveryConfigurationReferenceAndFirstStopIsVisible()
    {
        await using var fixture = await DeletionFixture.CreateAsync(DomainData.Rule(1, 1, localPort: 3101), DomainData.Rule(2, 1, localPort: 3102));
        await fixture.Profiles.StartAllAsync(DomainData.Profile(1).Id);
        await fixture.Activity.FlushAsync();
        AppState before = fixture.Configuration.Current!;
        var failing = fixture.Backend.Handles.Single(handle => handle.Actual.Local.Port == 3102);
        failing.OnDispose = () => throw new IOException("Synthetic disposal failure.");
        var result = await fixture.Deletion.DeleteConfirmedAsync(fixture.Deletion.CalcServerDeletionImpact([DomainData.Id(1)]));
        Assert.Equal(DeletionOutcome.StopFailed, result.Outcome); Assert.Single(result.StoppedTunnelIds);
        Assert.Equal(before.Servers, fixture.Configuration.Current!.Servers);
        Assert.Equal(before.Profiles, fixture.Configuration.Current.Profiles);
        Assert.Contains(fixture.Backend.Manager.Current.Tunnels, tunnel => tunnel.State == TunnelState.Stopped);
        Assert.Contains(fixture.Backend.Manager.Current.Tunnels, tunnel => tunnel.State == TunnelState.Stopping);
        failing.OnDispose = null;
        Assert.True((await fixture.Deletion.DeleteConfirmedAsync(fixture.Deletion.CalcServerDeletionImpact([DomainData.Id(1)]))).Succeeded);
    }

    [Fact]
    public async Task FailedSaveKeepsMemoryAndStoreThenRetryDoesNotRestartAnything()
    {
        await using var fixture = await DeletionFixture.CreateAsync(DomainData.Rule());
        await fixture.Profiles.StartAllAsync(DomainData.Profile(1).Id);
        await fixture.Activity.FlushAsync();
        AppState before = fixture.Configuration.Current!;
        fixture.Backend.Connections.Store.BeforeCommit = () => throw new IOException("Synthetic disk failure.");
        var result = await fixture.Deletion.DeleteConfirmedAsync(fixture.Deletion.CalcServerDeletionImpact([DomainData.Id(1)]));
        Assert.Equal(DeletionOutcome.SaveFailed, result.Outcome); Assert.Single(result.StoppedTunnelIds);
        Assert.Same(before, fixture.Configuration.Current); Assert.Same(before, fixture.Backend.Connections.Store.State);
        Assert.All(fixture.Backend.Handles, handle => Assert.False(handle.IsStarted));
        Assert.Equal(TunnelState.Stopped, Assert.Single(fixture.Backend.Manager.Current.Tunnels).State);
        fixture.Backend.Connections.Store.BeforeCommit = null;
        var retried = await fixture.Deletion.DeleteConfirmedAsync(fixture.Deletion.CalcServerDeletionImpact([DomainData.Id(1)]));
        Assert.True(retried.Succeeded); Assert.Single(fixture.Backend.Attempts);
        Assert.Single(fixture.Configuration.Current!.Servers); Assert.Empty(fixture.Configuration.Current.Profiles[0].Rules);
        Assert.Empty(fixture.Backend.Manager.Current.Tunnels);
        Assert.Single(fixture.Configuration.Current.RecentActivity.Where(entry => entry.Outcome == ActivityOutcome.Stopped));
        Assert.Equal(1, fixture.Configuration.Current.Statistics.TotalForwards);
    }

    [Fact]
    public async Task ProfileDeletionKeepsRunningForwardsServersAndReadableSourceSnapshots()
    {
        await using var fixture = await DeletionFixture.CreateAsync(DomainData.Rule());
        await fixture.Profiles.StartAllAsync(DomainData.Profile(1).Id);
        await fixture.Activity.FlushAsync();
        var result = await fixture.Deletion.DeleteConfirmedAsync(fixture.Deletion.CalcAllDeletionImpact(ResourceKind.Profile));
        Assert.True(result.Succeeded); Assert.Empty(fixture.Configuration.Current!.Profiles);
        Assert.Equal(2, fixture.Configuration.Current.Servers.Length);
        var tunnel = Assert.Single(fixture.Backend.Manager.Current.Tunnels);
        Assert.Equal(TunnelState.Running, tunnel.State);
        var item = new TunnelItem(tunnel, fixture.Configuration.Current, DateTimeOffset.UtcNow);
        Assert.False(item.CanOpenProfile); Assert.True(item.CanOpenServer); Assert.False(string.IsNullOrEmpty(item.Source));
        Assert.Equal(0, fixture.Backend.Handles.Single().StopCalls);
    }
}

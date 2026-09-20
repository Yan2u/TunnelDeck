using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Tests;

public sealed class DeletionConcurrencyTests
{
    [Fact]
    public async Task DeletingOneServerCancelsOnlyItsPendingProfileRules()
    {
        await using var fixture = await DeletionFixture.CreateAsync(DomainData.Rule(1, 1), DomainData.Rule(2, 2));
        using var first = new AsyncGate();
        using var second = new AsyncGate();
        fixture.Prompt = async (server, token) =>
        {
            await (server.Id == DomainData.Id(1) ? first : second).WaitAsync(token);
            return ConnectionFixture.Input;
        };
        var start = fixture.Profiles.StartAllAsync(DomainData.Profile(1).Id);
        await Task.WhenAll(first.Entered.Task, second.Entered.Task).WaitAsync(TimeSpan.FromSeconds(5));
        var impact = fixture.Deletion.CalcServerDeletionImpact([DomainData.Id(1)]);
        Assert.Equal(1, impact.PendingForwardCount); Assert.Empty(impact.Tunnels);
        var result = await fixture.Deletion.DeleteConfirmedAsync(impact).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Succeeded); Assert.False(start.IsCompleted);
        second.Release.TrySetResult();
        var finished = await start.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, finished.CancelledCount); Assert.Equal(1, finished.StartedCount);
        Assert.Equal(DomainData.Id(2), fixture.Configuration.Current!.Profiles.Single().Rules.Single().ServerId);
        Assert.All(fixture.Backend.Handles, handle => Assert.True(handle.IsStarted));
    }

    [Fact]
    public async Task WaitingQuickCredentialsAreCancelledAndBothSelectionsAreReconciled()
    {
        await using var fixture = await DeletionFixture.CreateAsync();
        await fixture.Configuration.UpdateAsync(state => state with
        { Settings = state.Settings with { DefaultServerId = DomainData.Id(1), PinnedQuickServerId = DomainData.Id(1) } });
        fixture.Dispatcher.Drain();
        fixture.Workspace.Quick.SelectedServerId = DomainData.Id(1);
        fixture.Workspace.Quick.PortText = "3210";
        using var prompt = new AsyncGate();
        fixture.Prompt = async (_, token) => { await prompt.WaitAsync(token); return ConnectionFixture.Input; };
        var quick = fixture.Workspace.SubmitQuickAsync();
        await fixture.Dispatcher.UntilAsync(() => prompt.Entered.Task.IsCompleted);
        var impact = fixture.Deletion.CalcServerDeletionImpact([DomainData.Id(1)]);
        Assert.Equal(1, impact.PendingForwardCount);
        var deletion = fixture.Deletion.DeleteConfirmedAsync(impact);
        await fixture.Dispatcher.RunAsync(Task.WhenAll(quick, deletion));
        Assert.True((await deletion).Succeeded); Assert.Empty(fixture.Backend.Attempts);
        Assert.NotEqual(DomainData.Id(1), fixture.Workspace.Quick.SelectedServerId);
        Assert.Null(fixture.Configuration.Current!.Settings.DefaultServerId);
        Assert.Null(fixture.Configuration.Current.Settings.PinnedQuickServerId);
    }

    [Fact]
    public async Task ConnectionTestWaitingForHostApprovalIsDrained()
    {
        await using var fixture = await DeletionFixture.CreateAsync();
        using var approval = new AsyncGate();
        fixture.Backend.Connections.Approval.Handle = async (_, token) => { await approval.WaitAsync(token); return true; };
        var test = fixture.Backend.Connections.Manager.TestAsync(DomainData.Server(), ConnectionFixture.Input);
        await approval.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var impact = fixture.Deletion.CalcServerDeletionImpact([DomainData.Id(1)]);
        Assert.Equal(1, impact.ConnectionTestCount);
        var result = await fixture.Deletion.DeleteConfirmedAsync(impact).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(result.Succeeded);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => test);
        Assert.Equal(0, fixture.Backend.Connections.Manager.TrackedConnectionCount);
    }

    [Fact]
    public async Task LateListenerIsOwnedUntilCleanupAndCannotAppearAfterDeletion()
    {
        await using var fixture = await DeletionFixture.CreateAsync(DomainData.Rule(mode: ForwardMode.Remote));
        using var bind = new AsyncGate();
        fixture.Backend.Start = async (session, rule, _) =>
        {
            await bind.WaitAsync(); // Deliberately return a native listener after cancellation.
            return fixture.Backend.CreateHandle(session, rule);
        };
        var start = fixture.Profiles.StartAllAsync(DomainData.Profile(1).Id);
        await bind.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var deletion = fixture.Deletion.DeleteConfirmedAsync(fixture.Deletion.CalcServerDeletionImpact([DomainData.Id(1)]));
        await Task.Delay(30);
        Assert.False(deletion.IsCompleted); Assert.Equal(2, fixture.Configuration.Current!.Servers.Length);
        bind.Release.TrySetResult();
        var result = await deletion.WaitAsync(TimeSpan.FromSeconds(5));
        await start;
        Assert.True(result.Succeeded);
        Assert.All(fixture.Backend.Handles, handle => { Assert.True(handle.Disposed); Assert.False(handle.IsStarted); });
        Assert.DoesNotContain(fixture.Backend.Manager.Current.Tunnels, tunnel => tunnel.ServerId == DomainData.Id(1) && DeletionImpactCalculator.IsLive(tunnel));
    }

    [Fact]
    public async Task DeletionBarrierRejectsStartsAndQueuedRuleSaveCannotRestoreDeletedReferences()
    {
        await using var fixture = await DeletionFixture.CreateAsync(DomainData.Rule());
        await fixture.Profiles.StartAllAsync(DomainData.Profile(1).Id);
        await fixture.Activity.FlushAsync();
        using var dispose = new AsyncGate();
        fixture.Backend.Handles.Single().OnDispose = () => dispose.WaitAsync();
        var oldLaunch = fixture.Backend.Launch(port: 3501);
        var deletion = fixture.Deletion.DeleteConfirmedAsync(fixture.Deletion.CalcServerDeletionImpact([DomainData.Id(1)]));
        await dispose.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ConnectionException>(() => fixture.Backend.Manager.StartAsync(oldLaunch, ConnectionFixture.Input));
        await Assert.ThrowsAsync<ConnectionException>(() => fixture.Profiles.StartAllAsync(DomainData.Profile(1).Id));
        var queued = fixture.Configuration.UpdateAsync(state => state with
        { Profiles = [state.Profiles[0] with { Rules = state.Profiles[0].Rules.Add(DomainData.Rule(2)) }] });
        dispose.Release.TrySetResult();
        Assert.True((await deletion.WaitAsync(TimeSpan.FromSeconds(5))).Succeeded);
        await Assert.ThrowsAsync<DomainValidationException>(() => queued);
        await Assert.ThrowsAsync<ConnectionException>(() => fixture.Backend.Manager.StartAsync(oldLaunch, ConnectionFixture.Input));
        Assert.Empty(fixture.Configuration.Current!.Profiles[0].Rules);
    }

    [Fact]
    public async Task ShutdownCancelsDeletionButWaitsForAcceptedCleanupWithoutRollingBackRuntime()
    {
        await using var fixture = await DeletionFixture.CreateAsync(DomainData.Rule());
        await fixture.Profiles.StartAllAsync(DomainData.Profile(1).Id);
        await fixture.Activity.FlushAsync();
        using var dispose = new AsyncGate();
        fixture.Backend.Handles.Single().OnDispose = () => dispose.WaitAsync();
        var deletion = fixture.Deletion.DeleteConfirmedAsync(fixture.Deletion.CalcServerDeletionImpact([DomainData.Id(1)]));
        await dispose.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var closing = fixture.Deletion.ShutdownAsync(CancellationToken.None);
        Assert.False(closing.IsCompleted);
        dispose.Release.TrySetResult();
        await closing.WaitAsync(TimeSpan.FromSeconds(5));
        var result = await deletion;
        Assert.Equal(DeletionOutcome.Cancelled, result.Outcome);
        Assert.Equal(2, fixture.Configuration.Current!.Servers.Length);
        Assert.Equal(TunnelState.Stopped, fixture.Backend.Manager.Current.Tunnels.Single().State);
    }

    [Fact]
    public async Task ProfileDeletionRetainsPreviouslyAcceptedPendingForwardSnapshots()
    {
        await using var fixture = await DeletionFixture.CreateAsync(DomainData.Rule());
        using var prompt = new AsyncGate();
        fixture.Prompt = async (_, token) => { await prompt.WaitAsync(token); return ConnectionFixture.Input; };
        var start = fixture.Profiles.StartAllAsync(DomainData.Profile(1).Id);
        await prompt.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var result = await fixture.Deletion.DeleteConfirmedAsync(fixture.Deletion.CalcAllDeletionImpact(ResourceKind.Profile));
        Assert.True(result.Succeeded); Assert.False(start.IsCompleted);
        prompt.Release.TrySetResult();
        Assert.Equal(1, (await start.WaitAsync(TimeSpan.FromSeconds(5))).StartedCount);
        Assert.Empty(fixture.Configuration.Current!.Profiles);
        Assert.Equal(TunnelState.Running, fixture.Backend.Manager.Current.Tunnels.Single().State);
    }
}

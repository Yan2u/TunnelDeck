using TunnelDeck.Core.Models;
using TunnelDeck.Core.Profiles;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class ProfileCancellationTests
{
    [Fact]
    public async Task CancelDrainsLateSuccessAndKeepsRulesThatAlreadyStarted()
    {
        await using var fixture = await ProfileFixture.CreateAsync(rules: [DomainData.Rule(), DomainData.Rule(2, 2, localPort: 4000)]);
        using var gate = new AsyncGate();
        var running = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runner.StateChanged += state => { if (state.StartedCount > 0) { running.TrySetResult(); } };
        fixture.Backend.Start = async (session, rule, token) =>
        {
            if (rule.ServerId == DomainData.Id(2))
            {
                using var registration = token.Register(() => cancelled.TrySetResult());
                await gate.WaitAsync(); // Deliberately return a handle after cancellation.
            }
            return fixture.Backend.CreateHandle(session, rule);
        };
        var work = fixture.Runner.StartAllAsync(fixture.Profile.Id);
        await Task.WhenAll(gate.Entered.Task, running.Task).WaitAsync(TimeSpan.FromSeconds(5));
        var cancel = fixture.Runner.CancelAsync(fixture.Profile.Id);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(cancel.IsCompleted);
        gate.Release.TrySetResult();
        await cancel;
        ProfileRunSnapshot result = await work;
        Assert.Equal(1, result.StartedCount); Assert.Equal(1, result.CancelledCount);
        Assert.NotNull(result.Rules.Single(rule => rule.Outcome == ProfileRuleOutcome.Cancelled).TunnelId);
        Assert.Equal(1, fixture.Backend.Manager.Current.RunningCount);
        Assert.Single(fixture.Backend.Handles, handle => handle.IsStarted);
        Assert.DoesNotContain(fixture.Backend.Manager.Current.Tunnels, tunnel => tunnel.State is TunnelState.Starting or TunnelState.Stopping);
    }

    [Fact]
    public async Task ShutdownWaitsForTheCredentialDialogAndCannotStartFromItsLateAnswer()
    {
        await using var fixture = await ProfileFixture.CreateAsync(rules: [DomainData.Rule()]);
        using var gate = new AsyncGate();
        fixture.Prompt = async (_, _) => { await gate.WaitAsync(); return ConnectionFixture.Input; };
        var start = fixture.Runner.StartAllAsync(fixture.Profile.Id);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var shutdown = fixture.Runner.ShutdownAsync(CancellationToken.None);
        Assert.False(shutdown.IsCompleted);
        gate.Release.TrySetResult(); await shutdown;
        Assert.Equal(1, (await start).CancelledCount);
        Assert.Empty(fixture.Backend.Manager.Current.Tunnels);
        Assert.Empty(fixture.Backend.Connections.Factory.Requests);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Runner.StartAllAsync(fixture.Profile.Id));
    }

    [Fact]
    public async Task CancellingOneProfileDoesNotCancelAnotherProfilesDuplicateWaiter()
    {
        await using var fixture = await ProfileFixture.CreateAsync(rules: [DomainData.Rule()]);
        var second = DomainData.Profile(2, DomainData.Rule(2));
        await fixture.Backend.Connections.Configuration.SaveProfileAsync(second, null);
        var secondRegistered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Runner.StateChanged += state => { if (state.ProfileId == second.Id && state.Rules.Any(rule => rule.TunnelId is not null)) { secondRegistered.TrySetResult(); } };
        using var gate = new AsyncGate();
        fixture.Backend.Start = async (session, rule, token) => { await gate.WaitAsync(token); return fixture.Backend.CreateHandle(session, rule); };
        var firstStart = fixture.Runner.StartAllAsync(fixture.Profile.Id);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var secondStart = fixture.Runner.StartAllAsync(second.Id);
        await secondRegistered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await fixture.Runner.CancelAsync(fixture.Profile.Id);
        Assert.Equal(1, (await firstStart).CancelledCount);
        gate.Release.TrySetResult();
        Assert.Equal(1, (await secondStart).AlreadyRunningCount);
        Assert.Single(fixture.Backend.Attempts);
        Assert.Equal(1, fixture.Backend.Manager.Current.RunningCount);
    }
}

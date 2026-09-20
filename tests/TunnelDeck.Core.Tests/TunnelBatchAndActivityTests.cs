using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Tests;

public sealed class TunnelBatchAndActivityTests
{
    [Fact]
    public async Task AServerSerializesControlOperationsWhileAnotherServerCanProgress()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        using var gate = new AsyncGate();
        fixture.Start = async (session, rule, token) =>
        {
            if (rule.ServerId == DomainData.Id(1) && rule.LocalPort == 3000) { await gate.WaitAsync(token); }
            return fixture.CreateHandle(session, rule);
        };
        Task<TunnelStartResult> first = fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<TunnelStartResult> sameServer = fixture.Manager.StartAsync(fixture.Launch(number: 2, port: 3001), ConnectionFixture.Input);
        TunnelStartResult independent = await fixture.Manager.StartAsync(fixture.Launch(number: 3, server: 2, port: 4000), ConnectionFixture.Input).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(independent.Succeeded);
        Assert.False(sameServer.IsCompleted);
        Assert.Single(fixture.Attempts, rule => rule.ServerId == DomainData.Id(1));
        Assert.True((await fixture.Manager.StopAsync(independent.Tunnel.Id)).Succeeded);
        gate.Release.TrySetResult();
        Assert.True((await first).Succeeded);
        Assert.True((await sameServer).Succeeded);
    }

    [Fact]
    public async Task StartAllPreservesOrderPartialSuccessAndDuplicateIdentity()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        fixture.Start = (session, rule, _) => rule.LocalPort == 3001
            ? Task.FromException<IForwardHandle>(new ForwardException(new(TunnelFailureKind.LocalAccessDenied, "Synthetic denied bind.")))
            : Task.FromResult<IForwardHandle>(fixture.CreateHandle(session, rule));
        var good = fixture.Launch();
        var bad = fixture.Launch(number: 2, port: 3001);
        var other = fixture.Launch(number: 3, server: 2, port: 4000);
        var duplicate = good with { Source = good.Source with { ProfileName = "Later profile" } };
        var results = await fixture.Manager.StartAllAsync([good, bad, other, duplicate], new Dictionary<Guid, ConnectionCredentials>
        {
            [DomainData.Id(1)] = ConnectionFixture.Input, [DomainData.Id(2)] = ConnectionFixture.Input,
        });
        Assert.Equal(new[] { good.Requested.Id, bad.Requested.Id, other.Requested.Id, duplicate.Requested.Id }, results.Select(item => item.Requested.Id));
        Assert.Equal(new[] { true, false, true, true }, results.Select(item => item.Succeeded));
        Assert.Equal(results[0].Result!.Tunnel.Id, results[3].Result!.Tunnel.Id);
        Assert.Equal(TunnelFailureKind.LocalAccessDenied, results[1].Failure!.Kind);
        Assert.Equal(2, fixture.Manager.Current.RunningCount);
        Assert.Equal(3, fixture.Attempts.Count);
    }

    [Fact]
    public async Task StartAllRespectsItsGlobalConcurrencyLimit()
    {
        RemoteServer[] servers = Enumerable.Range(1, 8).Select(number => DomainData.Server(number)).ToArray();
        await using var fixture = await TunnelFixture.CreateAsync(options: new() { MaximumConcurrentStarts = 2 }, servers: servers);
        int active = 0;
        int maximum = 0;
        fixture.Start = async (session, rule, token) =>
        {
            int current = Interlocked.Increment(ref active);
            int observed;
            do { observed = maximum; } while (current > observed && Interlocked.CompareExchange(ref maximum, current, observed) != observed);
            try { await Task.Delay(20, token); return fixture.CreateHandle(session, rule); }
            finally { Interlocked.Decrement(ref active); }
        };
        var inputs = servers.ToDictionary(server => server.Id, _ => ConnectionFixture.Input);
        var results = await fixture.Manager.StartAllAsync(Enumerable.Range(1, 8).Select(number => fixture.Launch(number, number, port: 3000 + number)), inputs);
        Assert.All(results, result => Assert.True(result.Succeeded));
        Assert.InRange(maximum, 1, 2);
        Assert.Equal(8, fixture.Attempts.Count);
    }

    [Fact]
    public async Task StopAllBlocksNewAdmissionUntilEveryPendingStartIsDrained()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        using var gate = new AsyncGate();
        fixture.Start = async (session, rule, _) => { var handle = fixture.CreateHandle(session, rule); await gate.WaitAsync(); return handle; };
        Task<TunnelStartResult> start = fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = fixture.Manager.StopAllAsync();
        await Assert.ThrowsAsync<ForwardException>(() => fixture.Manager.StartAsync(fixture.Launch(number: 2, server: 2, port: 4000), ConnectionFixture.Input));
        Assert.False(stop.IsCompleted);
        gate.Release.TrySetResult();
        Assert.All(await stop, result => Assert.True(result.Succeeded));
        Assert.False((await start).Succeeded);
        Assert.True((await fixture.Manager.StartAsync(fixture.Launch(number: 2, server: 2, port: 4000), ConnectionFixture.Input)).Succeeded);
    }

    [Fact]
    public async Task StopServerDoesNotStopOrBlockAnotherServer()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        TunnelStartResult first = await fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        TunnelStartResult other = await fixture.Manager.StartAsync(fixture.Launch(number: 2, server: 2, port: 4000), ConnectionFixture.Input);
        using var gate = new AsyncGate();
        fixture.Handles.Single(handle => handle.Actual.Local.Port == 3000).OnDispose = () => gate.WaitAsync();
        var stop = fixture.Manager.StopServerAsync(first.Tunnel.ServerId);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ForwardException>(() => fixture.Manager.StartAsync(fixture.Launch(number: 3, port: 3001), ConnectionFixture.Input));
        Assert.True((await fixture.Manager.StartAsync(fixture.Launch(number: 4, server: 2, port: 4001), ConnectionFixture.Input)).Succeeded);
        Assert.Equal(TunnelState.Running, fixture.Manager.Current.Tunnels.Single(tunnel => tunnel.Id == other.Tunnel.Id).State);
        gate.Release.TrySetResult();
        Assert.True((await stop).Single().Succeeded);
        Assert.Equal(2, fixture.Manager.Current.RunningCount);
    }

    [Fact]
    public async Task RepeatedRequestErrorsAreAggregatedWithoutFailingTheListener()
    {
        var clock = new TimerTimeProvider();
        await using var fixture = await TunnelFixture.CreateAsync(clock);
        await fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        int notifications = 0;
        fixture.Manager.StateChanged += snapshot =>
        {
            if (snapshot.Tunnels.Single().State == TunnelState.Running && snapshot.Tunnels.Single().LastRequestError is not null) { notifications++; }
        };
        var handle = fixture.Handles.Single();
        for (int count = 1; count <= 100; count++)
        {
            handle.Report(new() { AcceptedConnections = count, FailedConnections = count, LastRequestAt = clock.GetUtcNow(), LastFailureAt = clock.GetUtcNow(), LastFailure = new(TunnelFailureKind.ConnectionRequest, "A request failed.") });
        }
        ActiveTunnel current = fixture.Manager.Current.Tunnels.Single();
        Assert.Equal(TunnelState.Running, current.State);
        Assert.Null(current.LastError);
        Assert.Equal(100, current.RequestFailureCount);
        Assert.Equal(1, notifications);
        clock.Advance(TimeSpan.FromSeconds(2));
        handle.Report(handle.Activity with { FailedConnections = 101 });
        Assert.Equal(2, notifications);
        Assert.Equal(101, fixture.Manager.Current.Tunnels.Single().RequestFailureCount);
    }

    [Fact]
    public async Task OutOfOrderActivityCannotRegressCountsOrRepeatAnOldError()
    {
        var clock = new TimerTimeProvider();
        await using var fixture = await TunnelFixture.CreateAsync(clock);
        await fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        int notifications = 0;
        fixture.Manager.StateChanged += _ => notifications++;
        var handle = fixture.Handles.Single();
        handle.Report(new() { AcceptedConnections = 1, FailedConnections = 1, LastFailure = new(TunnelFailureKind.ConnectionRequest, "A request failed.") });
        ForwardActivity older = handle.Activity;
        clock.Advance(TimeSpan.FromSeconds(2));
        handle.Report(older with { AcceptedConnections = 2, FailedConnections = 2 });
        long revision = fixture.Manager.Current.Revision;
        handle.Replay(older);
        Assert.Equal(revision, fixture.Manager.Current.Revision);
        Assert.Equal(2, fixture.Manager.Current.Tunnels.Single().AcceptedConnections);
        Assert.Equal(2, fixture.Manager.Current.Tunnels.Single().RequestFailureCount);
        Assert.Equal(2, notifications);
        clock.Advance(TimeSpan.FromSeconds(2));
        handle.Report(handle.Activity with { AcceptedConnections = 3 });
        Assert.Equal(2, notifications);
    }

    [Fact]
    public async Task UnexpectedListenerStopIsCleanedUpAndMarkedFailed()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        await fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        var terminal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Manager.StateChanged += snapshot => { if (snapshot.Tunnels.Single().State == TunnelState.Failed) { terminal.TrySetResult(); } };
        fixture.Handles.Single().Report(new() { ListenerStopped = true });
        await terminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(TunnelFailureKind.Connectivity, fixture.Manager.Current.Tunnels.Single().LastError!.Kind);
        Assert.True(fixture.Handles.Single().Disposed);
    }
}

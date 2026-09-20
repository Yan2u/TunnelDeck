using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Tests;

public sealed class TunnelLifecycleTests
{
    [Fact]
    public async Task ConcurrentDuplicatesProduceOneInstanceAndPreserveTheFirstSource()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        using var gate = new AsyncGate();
        fixture.Start = async (session, rule, token) => { await gate.WaitAsync(token); return fixture.CreateHandle(session, rule); };
        var original = fixture.Launch();
        Task<TunnelStartResult> first = fixture.Manager.StartAsync(original, ConnectionFixture.Input);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = original with { Source = original.Source with { ProfileId = Guid.NewGuid(), ProfileName = "Later profile" }, Requested = original.Requested with { Id = Guid.NewGuid(), AutoFindAvailablePort = false } };
        Task<TunnelStartResult>[] others = Enumerable.Range(0, 8).Select(_ => fixture.Manager.StartAsync(duplicate, ConnectionFixture.Input)).ToArray();
        Assert.Single(fixture.Manager.Current.Tunnels);
        Assert.Single(fixture.Attempts);
        gate.Release.TrySetResult();
        TunnelStartResult initial = await first;
        Assert.False(initial.Reused);
        Assert.All(await Task.WhenAll(others), result => { Assert.True(result.Reused); Assert.Equal(initial.Tunnel.Id, result.Tunnel.Id); Assert.Equal(original.Source, result.Tunnel.Source); });
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("remove")]
    [InlineData("shutdown")]
    public async Task StopRemoveAndShutdownOwnAnUncooperativeLateStart(string operation)
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        using var gate = new AsyncGate();
        fixture.Start = async (session, rule, _) => { var handle = fixture.CreateHandle(session, rule); await gate.WaitAsync(); return handle; };
        Task<TunnelStartResult> start = fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Guid id = fixture.Manager.Current.Tunnels.Single().Id;
        Task stop = operation switch
        {
            "remove" => fixture.Manager.RemoveAsync(id),
            "shutdown" => fixture.Manager.ShutdownAsync(CancellationToken.None),
            _ => fixture.Manager.StopAsync(id),
        };
        await fixture.Connections.Factory.Sessions.Single().Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(stop.IsCompleted);
        Assert.False(start.IsCompleted);
        Assert.Equal(TunnelState.Stopping, fixture.Manager.Current.Tunnels.Single().State);
        gate.Release.TrySetResult();
        await stop;
        Assert.False((await start).Succeeded);
        Assert.True(fixture.Handles.Single().Disposed);
        Assert.Equal(0, fixture.Connections.Manager.TrackedConnectionCount);
        Assert.False(fixture.Connections.Manager.IsServerInUse(DomainData.Id(1)));
        if (operation == "remove") { Assert.Empty(fixture.Manager.Current.Tunnels); }
        else { Assert.Equal(TunnelState.Stopped, fixture.Manager.Current.Tunnels.Single().State); }
    }

    [Fact]
    public async Task CancellingOneDuplicateCallerDoesNotCancelTheOtherCaller()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        using var gate = new AsyncGate();
        fixture.Start = async (session, rule, token) => { await gate.WaitAsync(token); return fixture.CreateHandle(session, rule); };
        using var cancellation = new CancellationTokenSource();
        Task<TunnelStartResult> first = fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input, cancellation.Token);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<TunnelStartResult> second = fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.Equal(TunnelState.Starting, fixture.Manager.Current.Tunnels.Single().State);
        gate.Release.TrySetResult();
        Assert.True((await second).Succeeded);
        Assert.Single(fixture.Attempts);
    }

    [Fact]
    public async Task CancellingTheLastCallerDrainsItsLateHandle()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        using var gate = new AsyncGate();
        fixture.Start = async (session, rule, _) => { var handle = fixture.CreateHandle(session, rule); await gate.WaitAsync(); return handle; };
        using var cancellation = new CancellationTokenSource();
        Task<TunnelStartResult> start = fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input, cancellation.Token);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await fixture.Connections.Factory.Sessions.Single().Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(start.IsCompleted);
        gate.Release.TrySetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.Equal(TunnelState.Cancelled, fixture.Manager.Current.Tunnels.Single().State);
        Assert.True(fixture.Handles.Single().Disposed);
    }

    [Fact]
    public async Task DuplicateDuringStopWaitsForCleanupBeforeCreatingANewGeneration()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        TunnelStartResult first = await fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        using var gate = new AsyncGate();
        fixture.Handles.Single().OnStop = _ => gate.WaitAsync();
        Task<TunnelStopResult> stopping = fixture.Manager.StopAsync(first.Tunnel.Id);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<TunnelStartResult> restart = fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        Assert.False(restart.IsCompleted);
        Assert.Single(fixture.Attempts);
        gate.Release.TrySetResult();
        Assert.True((await stopping).Succeeded);
        TunnelStartResult second = await restart;
        Assert.True(second.Succeeded);
        Assert.NotEqual(first.Tunnel.Id, second.Tunnel.Id);
        Assert.Equal(2, fixture.Attempts.Count);
    }

    [Fact]
    public async Task IdentityCannotChangeUntilTheEntireStopHasFinished()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        var launch = fixture.Launch();
        TunnelStartResult started = await fixture.Manager.StartAsync(launch, ConnectionFixture.Input);
        using var gate = new AsyncGate();
        fixture.Handles.Single().OnDispose = () => gate.WaitAsync();
        Task<TunnelStopResult> stop = fixture.Manager.StopAsync(started.Tunnel.Id);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ConnectionException>(() => fixture.Connections.Manager.SaveServerAsync(launch.Server with { Host = "changed.example" }, CredentialChange.Forget));
        gate.Release.TrySetResult();
        await stop;
        await fixture.Connections.Manager.SaveServerAsync(launch.Server with { Host = "changed.example" }, CredentialChange.Forget);
        TunnelStartResult stale = await fixture.Manager.StartAsync(launch, ConnectionFixture.Input);
        Assert.Equal(TunnelFailureKind.Input, stale.Tunnel.LastError!.Kind);
        Assert.Single(fixture.Attempts);
    }

    [Fact]
    public async Task CleanupFailureRemainsStoppingAndCanBeRetriedWithoutLosingOwnership()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        TunnelStartResult started = await fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        var handle = fixture.Handles.Single();
        handle.OnDispose = () => handle.DisposeCalls == 1 ? Task.FromException(new IOException("Synthetic dispose failure.")) : Task.CompletedTask;
        TunnelStopResult failed = await fixture.Manager.StopAsync(started.Tunnel.Id);
        Assert.False(failed.Succeeded);
        Assert.Equal(TunnelState.Stopping, failed.Tunnel!.State);
        Assert.True(fixture.Connections.Manager.IsServerInUse(started.Tunnel.ServerId));
        Assert.True((await fixture.Manager.StopAsync(started.Tunnel.Id)).Succeeded);
        Assert.Equal(2, handle.DisposeCalls);
        Assert.False(fixture.Connections.Manager.IsServerInUse(started.Tunnel.ServerId));
        Assert.True((await fixture.Manager.StopAsync(started.Tunnel.Id)).Succeeded);
        Assert.Equal(2, handle.DisposeCalls);
    }

    [Fact]
    public async Task RetirementCancellationCannotReplaceAConfirmedRemoteRefusal()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        fixture.Start = async (session, _, token) =>
        {
            var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = token.Register(() => cancelled.TrySetResult());
            session.Disconnect();
            await cancelled.Task;
            throw new ForwardException(new(TunnelFailureKind.RemoteListenFailure, "The server refused remote listening."));
        };
        TunnelStartResult result = await fixture.Manager.StartAsync(fixture.Launch(mode: ForwardMode.Remote), ConnectionFixture.Input);
        Assert.Equal(TunnelState.Failed, result.Tunnel.State);
        Assert.Equal(TunnelFailureKind.RemoteListenFailure, result.Tunnel.LastError!.Kind);
        Assert.Equal(1, result.Tunnel.BindAttempts);
        Assert.Equal(0, fixture.Connections.Manager.TrackedConnectionCount);
    }

    [Fact]
    public async Task SharedTransportLossUpdatesEveryAffectedRule()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        await fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        await fixture.Manager.StartAsync(fixture.Launch(number: 2, port: 3001), ConnectionFixture.Input);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.Manager.StateChanged += snapshot => { if (snapshot.Tunnels.Length == 2 && snapshot.Tunnels.All(tunnel => tunnel.State == TunnelState.Failed)) { failed.TrySetResult(); } };
        fixture.Connections.Factory.Sessions.Single().Disconnect();
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, fixture.Manager.Current.RunningCount);
        Assert.All(fixture.Handles, handle => Assert.True(handle.Disposed));
        Assert.All(fixture.Manager.Current.Tunnels, tunnel => Assert.Equal(TunnelFailureKind.Connectivity, tunnel.LastError!.Kind));
    }
}

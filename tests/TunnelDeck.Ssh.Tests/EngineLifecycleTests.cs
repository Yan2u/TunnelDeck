using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tunnels;
using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class EngineLifecycleTests(SshFixture fixture)
{
    [Fact]
    public async Task AnIdleForwardedChannelRepliesToOpenSshAliveProbesAndStillTransfersData()
    {
        await using var echo = new LocalEchoServer();
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        int remotePort = await fixture.UnusedPortAsync();
        TunnelStartResult start = await scope.Tunnels.StartAsync(scope.Rule(echo.Port, remotePort, ForwardMode.Remote));
        Assert.True(start.Succeeded, start.Tunnel.LastError?.Message);
        byte[] payload = [1, 0, 255, 4, 8];
        var response = await fixture.PostAsync("probe", new { port = remotePort, payload = Convert.ToBase64String(payload), idleSeconds = 7 });
        Assert.Equal(payload, Convert.FromBase64String(response.GetProperty("payload").GetString()!));
        Assert.Equal(TunnelState.Running, scope.Tunnels.Current.Tunnels.Single().State);
        Assert.False(scope.Connections.Factory.Sessions.Single().Disconnected.IsCancellationRequested);
        Assert.True((await scope.Tunnels.StopAsync(start.Tunnel.Id)).Succeeded);
        await fixture.ExpectListeningAsync(remotePort, false);
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("cancel")]
    [InlineData("remove")]
    [InlineData("shutdown")]
    public async Task ARealRemoteListenerWithAHeldAcknowledgementCannotBecomeOrphaned(string action)
    {
        await using var proxy = new MultiConnectionSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        await using var echo = new LocalEchoServer();
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest, proxy.Port,
            connectionOptions: new() { ConnectTimeout = TimeSpan.FromSeconds(3), LibraryTimeout = TimeSpan.FromSeconds(5), ControlTimeout = TimeSpan.FromSeconds(2) },
            attemptTimeout: TimeSpan.FromSeconds(1));
        scope.Connections.Factory.SessionOpened = _ => proxy.HoldNewestReplies();
        using var cancellation = new CancellationTokenSource();
        int remotePort = await fixture.UnusedPortAsync();
        Task<TunnelStartResult> start = scope.Tunnels.StartAsync(scope.Rule(echo.Port, remotePort, ForwardMode.Remote), cancellationToken: cancellation.Token);
        await proxy.WaitForHeldReplyAsync();
        await fixture.ExpectListeningAsync(remotePort, true);
        Guid id = scope.Tunnels.Current.Tunnels.Single().Id;
        Task? cleanup = null;
        if (action == "cancel") { cancellation.Cancel(); }
        if (action == "remove") { cleanup = scope.Tunnels.RemoveAsync(id); }
        if (action == "shutdown") { cleanup = scope.Tunnels.ShutdownAsync(CancellationToken.None); }
        if (cleanup is not null) { await cleanup.WaitAsync(TimeSpan.FromSeconds(8)); }
        if (action == "cancel") { await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start); }
        else
        {
            TunnelStartResult result = await start;
            Assert.False(result.Succeeded);
            if (action == "timeout") { Assert.Equal(TunnelFailureKind.TimeoutUnknownOutcome, result.Tunnel.LastError!.Kind); }
        }
        proxy.ResumeReplies();
        await fixture.ExpectListeningAsync(remotePort, false);
        Assert.Equal(0, scope.Connections.Manager.TrackedConnectionCount);
        Assert.All(scope.Connections.Factory.Sessions, session => Assert.True(session.IsDisposed));
        if (action == "remove") { Assert.Empty(scope.Tunnels.Current.Tunnels); }
    }

    [Fact]
    public async Task AnUnconfirmedRemoteStopRetiresOnlyItsTransport()
    {
        await using var proxy = new MultiConnectionSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        using var block = LocalPortBlock.Reserve(1);
        block.Release(0);
        await using var echo = new LocalEchoServer();
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest, proxy.Port,
            options: new() { StopTimeout = TimeSpan.FromMilliseconds(500) });
        TunnelStartResult local = await scope.Tunnels.StartAsync(scope.Rule(block.First, fixture.Manifest.EchoPort));
        int firstPort = await fixture.UnusedPortAsync();
        int secondPort = await fixture.UnusedPortAsync();
        TunnelStartResult first = await scope.Tunnels.StartAsync(scope.Rule(echo.Port, firstPort, ForwardMode.Remote));
        TunnelStartResult second = await scope.Tunnels.StartAsync(scope.Rule(echo.Port, secondPort, ForwardMode.Remote));
        Assert.All(new[] { local, first, second }, result => Assert.True(result.Succeeded, result.Tunnel.LastError?.Message));
        proxy.HoldNewestReplies();
        Task<TunnelStopResult> stopping = scope.Tunnels.StopAsync(second.Tunnel.Id);
        await proxy.WaitForHeldReplyAsync();
        TunnelStopResult stopped = await stopping.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.True(stopped.Succeeded);
        Assert.Equal(TunnelFailureKind.TimeoutUnknownOutcome, stopped.Tunnel!.LastError!.Kind);
        proxy.ResumeReplies();
        await fixture.ExpectListeningAsync(secondPort, false);
        await LocalEchoServer.AssertPayloadAsync(block.First, [7, 8, 0, 1]);
        await fixture.AssertRemotePayloadAsync(firstPort, [7, 8, 0, 1]);
        Assert.Equal(2, scope.Tunnels.Current.RunningCount);
    }

    [Fact]
    public async Task ShutdownCoordinatorReleasesLocalAndRemoteListenersBeforeItCompletes()
    {
        using var block = LocalPortBlock.Reserve(2);
        block.Release(0);
        block.Release(1);
        await using var echo = new LocalEchoServer();
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        int remotePort = await fixture.UnusedPortAsync();
        var results = await scope.Tunnels.StartAllAsync([
            scope.Rule(block.First, fixture.Manifest.EchoPort),
            scope.Rule(block.First + 1, fixture.Manifest.EchoPort),
            scope.Rule(echo.Port, remotePort, ForwardMode.Remote),
        ]);
        Assert.All(results, result => Assert.True(result.Succeeded, result.Failure?.Message));
        await LocalEchoServer.AssertPayloadAsync(block.First, [1, 2, 3]);
        await fixture.AssertRemotePayloadAsync(remotePort, [1, 2, 3]);
        var shutdown = new ShutdownCoordinator([scope.Tunnels, scope.Connections.Manager, scope.Connections.Configuration]);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await shutdown.ShutdownAsync(deadline.Token);
        Assert.Equal(0, scope.Tunnels.Current.RunningCount);
        Assert.All(scope.Tunnels.Current.Tunnels, tunnel => Assert.Equal(TunnelState.Stopped, tunnel.State));
        LocalPortBlock.AssertReusable(block.First);
        LocalPortBlock.AssertReusable(block.First + 1);
        await fixture.ExpectListeningAsync(remotePort, false);
        Assert.Equal(0, scope.Connections.Manager.TrackedConnectionCount);
        Assert.All(scope.Connections.Factory.Sessions, session => Assert.True(session.IsDisposed));
    }
}

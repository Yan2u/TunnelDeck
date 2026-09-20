using System.Diagnostics;
using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class RecoveryCancellationTests(SshFixture fixture)
{
    [Theory]
    [InlineData("stop")]
    [InlineData("delete")]
    [InlineData("exit")]
    public async Task StopDeleteAndExitDuringRealBackoffPreventTheForwardFromReturning(string action)
    {
        await using var proxy = new MultiConnectionSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        await using var scope = await RecoveryTestScope.CreateAsync(fixture.Manifest, proxy.Port);
        using var ports = LocalPortBlock.Reserve(1); ports.Release(0);
        var original = await scope.Tunnels.StartAsync(scope.Backend.Rule(ports.First, fixture.Manifest.EchoPort));
        Assert.True(original.Succeeded, original.Tunnel.LastError?.Message);
        proxy.RejectConnections(true); proxy.DisconnectAll();
        await scope.WaitAsync(() => scope.Tunnel(original.Tunnel.Id).State == TunnelState.Reconnecting);
        await EndAsync(scope, original.Tunnel.Id, action);
        proxy.RejectConnections(false);
        await scope.WaitAsync(() => scope.Reconnect.ActiveServerCount == 0);
        int opens = scope.Connections.Factory.Opens;
        await Task.Delay(500); // Exceeds the fixture's maximum retry delay after cancellation.
        Assert.Equal(opens, scope.Connections.Factory.Opens);
        Assert.DoesNotContain(scope.Tunnels.Current.Tunnels, DeletionImpactCalculator.IsLive);
        Assert.Equal(0, scope.Connections.Manager.TrackedConnectionCount);
        Assert.All(scope.Connections.Factory.Sessions, session => Assert.True(session.IsDisposed));
        LocalPortBlock.AssertReusable(ports.First);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("delete")]
    [InlineData("exit")]
    public async Task AnUnacknowledgedRemoteRecoveryRemainsOwnedThroughStopDeleteAndExit(string action)
    {
        await using var proxy = new MultiConnectionSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        await using var echo = new LocalEchoServer();
        await using var scope = await RecoveryTestScope.CreateAsync(fixture.Manifest, proxy.Port, reconnect: new()
        { InitialDelay = TimeSpan.FromMilliseconds(500), MaximumDelay = TimeSpan.FromMilliseconds(500), RecoveryWindow = TimeSpan.FromSeconds(12), JitterFraction = 0 });
        int remotePort = await fixture.UnusedPortAsync();
        var original = await scope.Tunnels.StartAsync(scope.Backend.Rule(echo.Port, remotePort, ForwardMode.Remote));
        Assert.True(original.Succeeded, original.Tunnel.LastError?.Message);
        scope.Connections.Factory.SessionOpened = _ => proxy.HoldNewestReplies();
        proxy.DisconnectAll();
        await fixture.ExpectListeningAsync(remotePort, false);
        await proxy.WaitForHeldReplyAsync();
        await fixture.ExpectListeningAsync(remotePort, true);
        Assert.Equal(TunnelState.Reconnecting, scope.Tunnel(original.Tunnel.Id).State);
        Assert.Equal(2, scope.Tunnel(original.Tunnel.Id).Generation);
        await EndAsync(scope, original.Tunnel.Id, action);
        proxy.ResumeReplies();
        await scope.WaitAsync(() => scope.Reconnect.ActiveServerCount == 0);
        await fixture.ExpectListeningAsync(remotePort, false);
        Assert.DoesNotContain(scope.Tunnels.Current.Tunnels, DeletionImpactCalculator.IsLive);
        Assert.All(scope.Connections.Factory.Sessions, session => Assert.True(session.IsDisposed));
        Assert.Equal(0, scope.Connections.Manager.TrackedConnectionCount);
    }

    [Fact]
    public async Task ANetworkBlackholeReleasesLocalOwnershipBeforeTheServerDetectsTheLostPeer()
    {
        await using var proxy = new MultiConnectionSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        await using var echo = new LocalEchoServer();
        await using var scope = await RecoveryTestScope.CreateAsync(fixture.Manifest, proxy.Port,
            tunnels: new() { StopTimeout = TimeSpan.FromMilliseconds(200) });
        int remotePort = await fixture.UnusedPortAsync();
        var original = await scope.Tunnels.StartAsync(scope.Backend.Rule(echo.Port, remotePort, ForwardMode.Remote));
        Assert.True(original.Succeeded, original.Tunnel.LastError?.Message);
        await fixture.AssertRemotePayloadAsync(remotePort, [8, 0, 9]);
        proxy.BlackholeNewest();
        var elapsed = Stopwatch.StartNew();
        var stopped = await scope.Tunnels.StopAsync(original.Tunnel.Id).WaitAsync(TimeSpan.FromSeconds(8));
        long localReleaseMs = elapsed.ElapsedMilliseconds;
        Assert.True(stopped.Succeeded); Assert.Equal(TunnelFailureKind.TimeoutUnknownOutcome, stopped.Tunnel!.LastError!.Kind);
        Assert.Equal(0, scope.Connections.Manager.TrackedConnectionCount);
        Assert.All(scope.Connections.Factory.Sessions, session => Assert.True(session.IsDisposed));
        Assert.True(await fixture.IsListeningAsync(remotePort));
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await fixture.IsListeningAsync(remotePort)) { await Task.Delay(100, deadline.Token); }
        LifecycleEvidence.Write("blackhole.json", new
        {
            passed = true, localReleaseMs, remoteReleaseMs = elapsed.ElapsedMilliseconds,
            serverDetection = "OpenSSH ClientAliveInterval=2, ClientAliveCountMax=2; upstream FIN was intentionally not forwarded.",
        });
        Assert.InRange(elapsed.ElapsedMilliseconds, localReleaseMs + 1, 12000);
    }

    private static async Task EndAsync(RecoveryTestScope scope, Guid id, string action)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        if (action == "stop") { Assert.True((await scope.Tunnels.StopAsync(id, deadline.Token)).Succeeded); }
        else if (action == "delete")
        {
            var result = await scope.Deletion.DeleteConfirmedAsync(scope.Deletion.CalcServerDeletionImpact([scope.Backend.Server.Id]), deadline.Token);
            Assert.True(result.Succeeded);
        }
        else { await scope.Shutdown.ShutdownAsync(deadline.Token); }
    }
}

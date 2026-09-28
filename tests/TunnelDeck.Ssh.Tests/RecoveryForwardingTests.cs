using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class RecoveryForwardingTests(SshFixture fixture)
{
    [Fact]
    public async Task InterruptedSharedLocalAndIndependentRemoteTransportsRecoverAndAnotherServerKeepsTransferring()
    {
        await using var proxy = new MultiConnectionSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        await using var echo = new LocalEchoServer();
        await using var scope = await RecoveryTestScope.CreateAsync(fixture.Manifest, proxy.Port);
        using var ports = LocalPortBlock.Reserve(3);
        for (int index = 0; index < 3; index++) { ports.Release(index); }
        int remotePort = await fixture.UnusedPortAsync();
        var starts = await scope.Tunnels.StartAllAsync([
            scope.Backend.Rule(ports.First, fixture.Manifest.EchoPort),
            scope.Backend.Rule(ports.First + 1, fixture.Manifest.EchoPort),
            scope.Backend.Rule(echo.Port, remotePort, ForwardMode.Remote),
        ]);
        Assert.All(starts, result => Assert.True(result.Succeeded, result.Failure?.Message));
        var originals = scope.Tunnels.Current.Tunnels.ToDictionary(tunnel => tunnel.Id);
        Assert.Equal(2, scope.Connections.Manager.TrackedConnectionCount);
        var other = ConnectionTestScope.Server(fixture.Manifest);
        await scope.Connections.SaveAsync(other, fixture.Manifest.Password);
        other = scope.Connections.Configuration.Current!.Servers.Single(server => server.Id == other.Id);
        var unaffected = await scope.Tunnels.StartAsync(scope.Backend.Rule(ports.First + 2, fixture.Manifest.EchoPort, server: other));
        Assert.True(unaffected.Succeeded, unaffected.Tunnel.LastError?.Message);
        byte[] payload = [4, 0, 8, 255, 9];
        await LocalEchoServer.AssertPayloadAsync(ports.First, payload);
        await fixture.AssertRemotePayloadAsync(remotePort, payload);
        await scope.Activity.FlushAsync();
        long total = scope.Connections.Configuration.Current.Statistics.TotalForwards;

        proxy.RejectConnections(true);
        proxy.DisconnectAll();
        // Reconnecting is published before old handles finish cleanup and the coordinator can register its group.
        await scope.WaitAsync(() => originals.Keys.All(id => scope.Tunnel(id).State == TunnelState.Reconnecting) &&
            scope.Reconnect.ActiveServerCount == 1);
        Assert.Equal(1, scope.Reconnect.ActiveServerCount);
        await LocalEchoServer.AssertPayloadAsync(ports.First + 2, payload);
        Assert.Equal(unaffected.Tunnel.Generation, scope.Tunnel(unaffected.Tunnel.Id).Generation);
        proxy.RejectConnections(false);
        await scope.WaitAsync(() => originals.Keys.All(id => scope.Tunnel(id).IsRunning) && scope.Reconnect.ActiveServerCount == 0);

        await LocalEchoServer.AssertPayloadAsync(ports.First, payload);
        await LocalEchoServer.AssertPayloadAsync(ports.First + 1, payload);
        await fixture.AssertRemotePayloadAsync(remotePort, payload);
        Assert.Equal(3, scope.Connections.Manager.TrackedConnectionCount);
        foreach (var (id, original) in originals)
        {
            var restored = scope.Tunnel(id);
            Assert.Equal(original.Requested, restored.Requested); Assert.Equal(original.Source, restored.Source);
            Assert.Equal(original.StartedAt, restored.StartedAt); Assert.Equal(original.Actual, restored.Actual);
            Assert.Equal(1, restored.ReconnectCount); Assert.Null(restored.EndedAt);
        }
        await scope.Activity.FlushAsync();
        Assert.Equal(total, scope.Connections.Configuration.Current.Statistics.TotalForwards);
        Assert.DoesNotContain(scope.Connections.Configuration.Current.RecentActivity, entry => entry.Outcome == ActivityOutcome.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryPrefersTheActualLocalPortAndOnlyIncrementsOnAConfirmedCollision(bool occupyPrevious)
    {
        await using var proxy = new MultiConnectionSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        await using var scope = await RecoveryTestScope.CreateAsync(fixture.Manifest, proxy.Port, reconnect: new()
        { InitialDelay = TimeSpan.FromSeconds(1), MaximumDelay = TimeSpan.FromSeconds(1), RecoveryWindow = TimeSpan.FromSeconds(12), JitterFraction = 0 });
        using var ports = LocalPortBlock.Reserve(3);
        ports.Release(1); ports.Release(2);
        var launch = scope.Backend.Rule(ports.First, fixture.Manifest.EchoPort);
        var original = await scope.Tunnels.StartAsync(launch);
        Assert.True(original.Succeeded, original.Tunnel.LastError?.Message);
        Assert.Equal(ports.First + 1, original.Tunnel.Actual!.Local.Port);
        proxy.DisconnectAll();
        await scope.WaitAsync(() => scope.Tunnel(original.Tunnel.Id).NextReconnectAt is not null);
        ports.Release(0); // Requested is now free, but this instance must still prefer its last Actual.
        var collision = new TcpListener(IPAddress.Loopback, ports.First + 1) { ExclusiveAddressUse = false };
        try
        {
            if (occupyPrevious) { collision.Start(); }
            await scope.WaitAsync(() => scope.Tunnel(original.Tunnel.Id).IsRunning);
            Assert.Equal(ports.First + (occupyPrevious ? 2 : 1), scope.Tunnel(original.Tunnel.Id).Actual!.Local.Port);
            Assert.Equal(ports.First, scope.Tunnel(original.Tunnel.Id).Requested.LocalPort);
            await LocalEchoServer.AssertPayloadAsync(scope.Tunnel(original.Tunnel.Id).Actual!.Local.Port, [5, 0, 4, 255]);
            Assert.True(await scope.Tunnels.RemoveAsync(original.Tunnel.Id));
            var manual = await scope.Tunnels.StartAsync(launch);
            Assert.True(manual.Succeeded, manual.Tunnel.LastError?.Message);
            Assert.NotEqual(original.Tunnel.Id, manual.Tunnel.Id); Assert.Equal(ports.First, manual.Tunnel.Actual!.Local.Port);
        }
        finally { collision.Stop(); }
    }

    [Fact]
    public async Task ARemoteRecoveryRefusalDoesNotMoveThePortOrPreventLocalRecovery()
    {
        await using var proxy = new MultiConnectionSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        await using var echo = new LocalEchoServer();
        await using var scope = await RecoveryTestScope.CreateAsync(fixture.Manifest, proxy.Port, reconnect: new()
        { InitialDelay = TimeSpan.FromSeconds(1), MaximumDelay = TimeSpan.FromSeconds(1), RecoveryWindow = TimeSpan.FromSeconds(12), JitterFraction = 0 });
        using var ports = LocalPortBlock.Reserve(1); ports.Release(0);
        int remotePort = await fixture.UnusedPortAsync();
        var local = await scope.Tunnels.StartAsync(scope.Backend.Rule(ports.First, fixture.Manifest.EchoPort));
        var remote = await scope.Tunnels.StartAsync(scope.Backend.Rule(echo.Port, remotePort, ForwardMode.Remote));
        Assert.True(local.Succeeded, local.Tunnel.LastError?.Message); Assert.True(remote.Succeeded, remote.Tunnel.LastError?.Message);
        proxy.DisconnectAll();
        await scope.WaitAsync(() => scope.Tunnel(remote.Tunnel.Id).NextReconnectAt is not null);
        await fixture.ExpectListeningAsync(remotePort, false);
        await fixture.ReservePortAsync(remotePort);
        try
        {
            await scope.WaitAsync(() => scope.Tunnel(local.Tunnel.Id).IsRunning && scope.Tunnel(remote.Tunnel.Id).State == TunnelState.Failed && scope.Reconnect.ActiveServerCount == 0);
            Assert.Equal(TunnelFailureKind.RemoteListenFailure, scope.Tunnel(remote.Tunnel.Id).LastError!.Kind);
            Assert.Equal(remotePort, scope.Tunnel(remote.Tunnel.Id).Requested.RemotePort);
            Assert.Equal(remotePort, scope.Tunnel(remote.Tunnel.Id).Actual!.Remote.Port);
            Assert.Equal(1, scope.Tunnel(remote.Tunnel.Id).ReconnectAttempt);
            Assert.Equal(1, scope.Connections.Manager.TrackedConnectionCount);
            await LocalEchoServer.AssertPayloadAsync(ports.First, [6, 0, 3]);
        }
        finally { await fixture.ReleasePortAsync(remotePort); }
    }

    [Fact]
    public async Task RestartingTheSshDaemonRestoresOnlyThePreviouslyEstablishedForward()
    {
        await using var scope = await RecoveryTestScope.CreateAsync(fixture.Manifest, fixture.Manifest.MutablePort);
        using var ports = LocalPortBlock.Reserve(2); ports.Release(0); ports.Release(1);
        var running = await scope.Tunnels.StartAsync(scope.Backend.Rule(ports.First, fixture.Manifest.EchoPort));
        var stopped = await scope.Tunnels.StartAsync(scope.Backend.Rule(ports.First + 1, fixture.Manifest.EchoPort));
        Assert.True(running.Succeeded, running.Tunnel.LastError?.Message); Assert.True(stopped.Succeeded, stopped.Tunnel.LastError?.Message);
        Assert.True((await scope.Tunnels.StopAsync(stopped.Tunnel.Id)).Succeeded);
        var seen = new ConcurrentQueue<TunnelState>();
        scope.Tunnels.StateChanged += snapshot => seen.Enqueue(snapshot.Tunnels.Single(tunnel => tunnel.Id == running.Tunnel.Id).State);
        await fixture.PostAsync("restart", new { server = "mutable", disconnectClients = true });
        await scope.WaitAsync(() => scope.Tunnel(running.Tunnel.Id).ReconnectCount == 1 && scope.Tunnel(running.Tunnel.Id).IsRunning);
        Assert.Contains(TunnelState.Reconnecting, seen);
        await LocalEchoServer.AssertPayloadAsync(ports.First, [7, 2, 0]);
        Assert.Equal(TunnelState.Stopped, scope.Tunnel(stopped.Tunnel.Id).State);
        LocalPortBlock.AssertReusable(ports.First + 1);
    }

    [Theory]
    [InlineData("authentication", TunnelFailureKind.Credentials)]
    [InlineData("host-key", TunnelFailureKind.ChangedHostKey)]
    public async Task PermanentSecurityFailuresEndRecoveryWithoutAnotherTrustPrompt(string fault, TunnelFailureKind expected)
    {
        await using var proxy = new MultiConnectionSshProxy(fixture.Manifest.Host, fixture.Manifest.MutablePort);
        await using var scope = await RecoveryTestScope.CreateAsync(fixture.Manifest, proxy.Port);
        using var ports = LocalPortBlock.Reserve(1); ports.Release(0);
        var original = await scope.Tunnels.StartAsync(scope.Backend.Rule(ports.First, fixture.Manifest.EchoPort));
        Assert.True(original.Succeeded, original.Tunnel.LastError?.Message);
        int approvals = scope.Connections.Approval.Requests.Count;
        try
        {
            if (fault == "authentication")
            { scope.Connections.Credentials.Write(scope.Backend.Server.CredentialReference!.Value, "intentionally-invalid-fixture-input"); }
            else { await fixture.PostAsync("restart", new { server = "mutable", alternateKey = true }); }
            proxy.DisconnectAll();
            await scope.WaitAsync(() => scope.Tunnel(original.Tunnel.Id).State == TunnelState.Failed && scope.Reconnect.ActiveServerCount == 0);
            Assert.Equal(expected, scope.Tunnel(original.Tunnel.Id).LastError!.Kind);
            Assert.Equal(approvals, scope.Connections.Approval.Requests.Count);
            Assert.Equal(0, scope.Connections.Manager.TrackedConnectionCount);
            LocalPortBlock.AssertReusable(ports.First);
        }
        finally { if (fault == "host-key") { await fixture.PostAsync("restart", new { server = "mutable" }); } }
    }
}

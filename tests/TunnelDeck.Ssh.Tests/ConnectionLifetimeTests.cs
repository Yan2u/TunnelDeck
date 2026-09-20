using System.Net;
using System.Net.Sockets;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Security;
using TunnelDeck.Infrastructure.Ssh;
using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class ConnectionLifetimeTests(SshFixture fixture)
{
    [Fact]
    public async Task FactoryBackedSessionsExposeRealForwardRoutesAndIndependentStop()
    {
        await using var scope = new ConnectionTestScope();
        RemoteServer server = ConnectionTestScope.Server(fixture.Manifest);
        await scope.SaveAsync(server, fixture.Manifest.Password);
        await using SshSessionLease local = await scope.Manager.AcquireAsync(server.Id, ForwardMode.Local);
        await using SshSessionLease remote = await scope.Manager.AcquireAsync(server.Id, ForwardMode.Remote);
        await using var echo = new LocalEchoServer();
        var firstRule = new ForwardRule
        {
            Id = Guid.NewGuid(), ServerId = server.Id, Mode = ForwardMode.Local,
            LocalPort = UnusedLocalPort(), RemotePort = fixture.Manifest.EchoPort,
        };
        await using IForwardHandle first = await local.Session.StartAsync(firstRule);
        await using IForwardHandle second = await local.Session.StartAsync(firstRule with { Id = Guid.NewGuid(), LocalPort = UnusedLocalPort() });
        int remotePort = await fixture.UnusedPortAsync();
        await using IForwardHandle remoteHandle = await remote.Session.StartAsync(new()
        {
            Id = Guid.NewGuid(), ServerId = server.Id, Mode = ForwardMode.Remote,
            LocalPort = echo.Port, RemotePort = remotePort,
        });
        byte[] payload = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32768);
        await LocalEchoServer.AssertPayloadAsync(first.Actual.Local.Port, payload);
        await LocalEchoServer.AssertPayloadAsync(second.Actual.Local.Port, payload);
        await fixture.AssertRemotePayloadAsync(remotePort, payload);
        await first.StopAsync();
        Assert.False(first.IsStarted);
        await LocalEchoServer.AssertPayloadAsync(second.Actual.Local.Port, payload);
        await fixture.AssertRemotePayloadAsync(remotePort, payload);
        await remoteHandle.StopAsync();
        await fixture.ExpectListeningAsync(remotePort, false);
        var rebound = new TcpListener(IPAddress.Loopback, first.Actual.Local.Port);
        try { rebound.Start(); }
        finally { rebound.Stop(); }
    }

    [Fact]
    public async Task RealLocalAcquisitionsCoalesceAndRemoteTransportsRemainIndependent()
    {
        await using var scope = new ConnectionTestScope();
        RemoteServer server = ConnectionTestScope.Server(fixture.Manifest);
        await scope.SaveAsync(server, fixture.Manifest.Password);
        SshSessionLease[] local = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => scope.Manager.AcquireAsync(server.Id, ForwardMode.Local)));
        try
        {
            Assert.All(local, lease => Assert.Same(local[0].Session, lease.Session));
            Assert.Equal(2, scope.Factory.Opens);
            await using SshSessionLease remote1 = await scope.Manager.AcquireAsync(server.Id, ForwardMode.Remote);
            await using SshSessionLease remote2 = await scope.Manager.AcquireAsync(server.Id, ForwardMode.Remote);
            Assert.NotSame(remote1.Session, remote2.Session);
            Assert.NotSame(remote1.Session, local[0].Session);
            await remote1.DisposeAsync();
            Assert.False(remote2.Session.Disconnected.IsCancellationRequested);
            Assert.False(local[0].Session.Disconnected.IsCancellationRequested);
        }
        finally { foreach (SshSessionLease lease in local) { await lease.DisposeAsync(); } }
        Assert.All(scope.Factory.Sessions, session => Assert.True(session.IsDisposed));
        Assert.Equal(0, scope.Manager.TrackedConnectionCount);
        Assert.False(scope.Manager.IsServerInUse(server.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealHandshakeCanTimeOutOrBeCancelledWithoutReturningALateSession(bool cancel)
    {
        await using var proxy = new ControlledSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        proxy.HoldServerReplies();
        using var cancellation = new CancellationTokenSource();
        var factory = new SshSessionFactory(new()
        {
            ConnectTimeout = TimeSpan.FromSeconds(1), LibraryTimeout = TimeSpan.FromSeconds(3),
            ControlTimeout = TimeSpan.FromMilliseconds(500),
        });
        RemoteServer server = ConnectionTestScope.Server(fixture.Manifest, proxy.Port);
        Task<ISshSession> connecting = factory.OpenAsync(new(server, ForwardMode.Local, new(fixture.Manifest.Password), null), cancellation.Token);
        await proxy.WaitForHeldChunkAsync();
        if (cancel) { cancellation.Cancel(); }
        Exception? error = await Record.ExceptionAsync(() => connecting.WaitAsync(TimeSpan.FromSeconds(5)));
        if (cancel) { Assert.IsAssignableFrom<OperationCanceledException>(error); }
        else { Assert.Equal(ConnectionFailure.TimedOut, Assert.IsType<ConnectionException>(error).Failure); }
    }

    [Fact]
    public async Task ShutdownCancelsARealPendingHandshakeAndDrainsAllConnectionWork()
    {
        await using var proxy = new ControlledSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        proxy.HoldServerReplies();
        await using var scope = new ConnectionTestScope();
        Task<ConnectionTestResult> connecting = scope.Manager.TestAsync(ConnectionTestScope.Server(fixture.Manifest, proxy.Port), new(fixture.Manifest.Password));
        await proxy.WaitForHeldChunkAsync();
        await scope.Manager.ShutdownAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => connecting);
        Assert.Empty(scope.Factory.Sessions);
        Assert.Equal(0, scope.Manager.TrackedConnectionCount);
        Assert.Empty(scope.Approval.Requests);
    }

    [Fact]
    public async Task LostRealTransportSignalsDisconnectionToItsOwner()
    {
        var proxy = new ControlledSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        bool proxyDisposed = false;
        try
        {
            RemoteServer server = ConnectionTestScope.Server(fixture.Manifest, proxy.Port);
            var trusted = new HostKeyIdentity(server.Host, server.Port, "ssh-ed25519", fixture.Manifest.HostKeyFingerprint);
            await using ISshSession session = await new SshSessionFactory().OpenAsync(new(server, ForwardMode.Local, new(fixture.Manifest.Password), trusted));
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenRegistration registration = session.Disconnected.Register(() => disconnected.TrySetResult());
            await proxy.DisposeAsync();
            proxyDisposed = true;
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(session.Disconnected.IsCancellationRequested);
        }
        finally { if (!proxyDisposed) { await proxy.DisposeAsync(); } }
    }

    [Fact]
    public async Task DnsFailureIsReportedWithoutRawSocketDetails()
    {
        RemoteServer server = ConnectionTestScope.Server(fixture.Manifest) with { Host = $"tunneldeck-{Guid.NewGuid():N}.invalid" };
        ConnectionException error = await Assert.ThrowsAsync<ConnectionException>(() => new SshSessionFactory().OpenAsync(
            new(server, ForwardMode.Local, new(fixture.Manifest.Password), null)));
        Assert.Equal(ConnectionFailure.NameResolutionFailed, error.Failure);
        Assert.Null(error.InnerException);
    }

    private static int UnusedLocalPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try { listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
        finally { listener.Stop(); }
    }
}

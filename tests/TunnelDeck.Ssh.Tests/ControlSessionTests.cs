using System.Net;
using Renci.SshNet;
using TunnelDeck.Infrastructure.Ssh;
using TunnelDeck.Ssh.Tests.Fixture;
using Xunit.Abstractions;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class ControlSessionTests(SshFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task UnclassifiedRemoteRefusalRetiresTheTransportWithoutRetrying()
    {
        int occupied = await fixture.ReservePortAsync();
        try
        {
            using TestConnection connection = fixture.CreateConnection(timeout: TimeSpan.FromSeconds(5));
            await connection.Client.ConnectAsync(CancellationToken.None);
            await using var session = new SshControlSession(connection.Client, TimeSpan.FromSeconds(1));
            using var refused = new ForwardedPortRemote(IPAddress.Loopback, (uint)occupied, IPAddress.Loopback, 9);
            await Assert.ThrowsAsync<Renci.SshNet.Common.SshException>(() => session.StartAsync(refused));
            Assert.True(session.IsRetired);
            int nextPort = await fixture.UnusedPortAsync();
            using var next = new ForwardedPortRemote(IPAddress.Loopback, (uint)nextPort, IPAddress.Loopback, 9);
            await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(next));
            Assert.False(await fixture.IsListeningAsync(nextPort));
        }
        finally
        {
            await fixture.ReleasePortAsync(occupied);
        }
    }

    [Fact]
    public async Task IsolatedRemoteStopWaitsForItsOwnAcknowledgement()
    {
        await using var proxy = new ControlledSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        using TestConnection connection = fixture.CreateConnection(proxy.Port, timeout: TimeSpan.FromSeconds(5));
        await connection.Client.ConnectAsync(CancellationToken.None);
        await using var session = new SshControlSession(connection.Client, TimeSpan.FromSeconds(2));
        int port = await fixture.UnusedPortAsync();
        using var remote = new ForwardedPortRemote(IPAddress.Loopback, (uint)port, IPAddress.Loopback, 9);
        await session.StartAsync(remote);
        proxy.HoldServerReplies();
        Task stop = session.StopAsync(remote);
        TaskCompletionSource reply = await proxy.WaitForHeldChunkAsync();
        Assert.NotSame(stop, await Task.WhenAny(stop, Task.Delay(150)));
        reply.TrySetResult();
        proxy.ResumeServerReplies();
        await stop;
        Assert.False(session.IsRetired);
        await session.StopAsync(remote);
        await fixture.ExpectListeningAsync(port, false);
        output.WriteLine("ADAPTER: a dedicated Remote transport does not finish Stop until its own real sshd acknowledgement arrives; repeated Stop is harmless.");
    }

    [Fact]
    public async Task TwoLocalsShareAndTwoRemotesAreIndependentEvenWhenOneStopTimesOut()
    {
        using TestConnection localConnection = fixture.CreateConnection(timeout: TimeSpan.FromSeconds(5));
        using TestConnection secondRemoteConnection = fixture.CreateConnection(timeout: TimeSpan.FromSeconds(5));
        await using var proxy = new ControlledSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        using TestConnection firstRemoteConnection = fixture.CreateConnection(proxy.Port, timeout: TimeSpan.FromSeconds(5));
        await Task.WhenAll(localConnection.Client.ConnectAsync(CancellationToken.None),
            firstRemoteConnection.Client.ConnectAsync(CancellationToken.None), secondRemoteConnection.Client.ConnectAsync(CancellationToken.None));
        await using var localSession = new SshControlSession(localConnection.Client, TimeSpan.FromSeconds(1));
        await using var firstRemoteSession = new SshControlSession(firstRemoteConnection.Client, TimeSpan.FromSeconds(1));
        await using var secondRemoteSession = new SshControlSession(secondRemoteConnection.Client, TimeSpan.FromSeconds(1));
        await using var echo = new LocalEchoServer();
        using var firstLocal = new ForwardedPortLocal("127.0.0.1", 0, "127.0.0.1", (uint)fixture.Manifest.EchoPort);
        using var secondLocal = new ForwardedPortLocal("127.0.0.1", 0, "127.0.0.1", (uint)fixture.Manifest.EchoPort);
        int firstPort = await fixture.UnusedPortAsync();
        int secondPort = await fixture.UnusedPortAsync();
        using var firstRemote = new ForwardedPortRemote(IPAddress.Loopback, (uint)firstPort, IPAddress.Loopback, (uint)echo.Port);
        using var secondRemote = new ForwardedPortRemote(IPAddress.Loopback, (uint)secondPort, IPAddress.Loopback, (uint)echo.Port);
        await Task.WhenAll(localSession.StartAsync(firstLocal), localSession.StartAsync(secondLocal),
            firstRemoteSession.StartAsync(firstRemote), secondRemoteSession.StartAsync(secondRemote));
        byte[] payload = [0, 1, 255, 3, 4, 0, 6, 7];
        await LocalEchoServer.AssertPayloadAsync((int)firstLocal.BoundPort, payload);
        await LocalEchoServer.AssertPayloadAsync((int)secondLocal.BoundPort, payload);
        await fixture.AssertRemotePayloadAsync(firstPort, payload);
        await fixture.AssertRemotePayloadAsync(secondPort, payload);
        proxy.HoldServerReplies();
        Task stop = firstRemoteSession.StopAsync(firstRemote);
        await proxy.WaitForHeldChunkAsync();
        await Assert.ThrowsAsync<TimeoutException>(() => stop);
        Assert.True(firstRemoteSession.IsRetired);
        proxy.ResumeServerReplies();
        await fixture.ExpectListeningAsync(firstPort, false);
        await LocalEchoServer.AssertPayloadAsync((int)firstLocal.BoundPort, payload);
        await LocalEchoServer.AssertPayloadAsync((int)secondLocal.BoundPort, payload);
        await fixture.AssertRemotePayloadAsync(secondPort, payload);
        Assert.False(localSession.IsRetired);
        Assert.False(secondRemoteSession.IsRetired);
        await Task.WhenAll(localSession.StopAsync(firstLocal), localSession.StopAsync(secondLocal), secondRemoteSession.StopAsync(secondRemote));
        await fixture.ExpectListeningAsync(secondPort, false);
        output.WriteLine("ADAPTER: 2 Local rules share one transport; 2 Remote rules use one each. A remote Stop deadline retires only that Remote transport; the other three routes still transfer payload.");
    }

    [Fact]
    public async Task GuardRejectsASecondRemoteOnTheSameTransport()
    {
        using TestConnection connection = fixture.CreateConnection(timeout: TimeSpan.FromSeconds(5));
        await connection.Client.ConnectAsync(CancellationToken.None);
        await using var session = new SshControlSession(connection.Client, TimeSpan.FromSeconds(1));
        int firstPort = await fixture.UnusedPortAsync();
        int secondPort = await fixture.UnusedPortAsync();
        using var first = new ForwardedPortRemote(IPAddress.Loopback, (uint)firstPort, IPAddress.Loopback, 9);
        using var second = new ForwardedPortRemote(IPAddress.Loopback, (uint)secondPort, IPAddress.Loopback, 9);
        using var local = new ForwardedPortLocal("127.0.0.1", 0, "127.0.0.1", 9);
        await session.StartAsync(first);
        await session.StartAsync(first);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(second));
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(local));
        Assert.Single(connection.Client.ForwardedPorts);
        Assert.False(await fixture.IsListeningAsync(secondPort));
        await session.StopAsync(first);
        await fixture.ExpectListeningAsync(firstPort, false);
    }
}

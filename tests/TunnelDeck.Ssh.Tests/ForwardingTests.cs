using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Renci.SshNet;
using Renci.SshNet.Common;
using TunnelDeck.Ssh.Tests.Fixture;
using Xunit.Abstractions;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class ForwardingTests(SshFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public async Task TwoLocalAndTwoRemoteForwardsShareOneSessionAndStopIndependently()
    {
        using TestConnection connection = fixture.CreateConnection();
        await connection.Client.ConnectAsync(CancellationToken.None);
        await using var localEcho = new LocalEchoServer();
        int firstRemotePort = await fixture.UnusedPortAsync();
        int secondRemotePort = await fixture.UnusedPortAsync();
        using var firstLocal = new ForwardedPortLocal("127.0.0.1", 0, "127.0.0.1", (uint)fixture.Manifest.EchoPort);
        using var secondLocal = new ForwardedPortLocal("127.0.0.1", 0, "127.0.0.1", (uint)fixture.Manifest.EchoPort);
        using var firstRemote = new ForwardedPortRemote(IPAddress.Loopback, (uint)firstRemotePort, IPAddress.Loopback, (uint)localEcho.Port);
        using var secondRemote = new ForwardedPortRemote(IPAddress.Loopback, (uint)secondRemotePort, IPAddress.Loopback, (uint)localEcho.Port);
        foreach (ForwardedPort port in new ForwardedPort[] { firstLocal, secondLocal, firstRemote, secondRemote })
        {
            connection.Client.AddForwardedPort(port);
            port.Start();
            Assert.True(port.IsStarted);
        }

        byte[] payload = RandomNumberGenerator.GetBytes(32768);
        await LocalEchoServer.AssertPayloadAsync((int)firstLocal.BoundPort, payload);
        await LocalEchoServer.AssertPayloadAsync((int)secondLocal.BoundPort, payload);
        await fixture.AssertRemotePayloadAsync(firstRemotePort, payload);
        await fixture.AssertRemotePayloadAsync(secondRemotePort, payload);
        firstRemote.Stop();
        firstRemote.Stop();
        await fixture.ExpectListeningAsync(firstRemotePort, false);
        await LocalEchoServer.AssertPayloadAsync((int)firstLocal.BoundPort, payload);
        await LocalEchoServer.AssertPayloadAsync((int)secondLocal.BoundPort, payload);
        await fixture.AssertRemotePayloadAsync(secondRemotePort, payload);
        firstLocal.Stop();
        await LocalEchoServer.AssertPayloadAsync((int)secondLocal.BoundPort, payload);
        await fixture.AssertRemotePayloadAsync(secondRemotePort, payload);
        secondLocal.Stop();
        secondRemote.Stop();
        await fixture.ExpectListeningAsync(secondRemotePort, false);
        output.WriteLine("SSH-01: 32 KiB binary payload passed through 2 Local + 2 Remote forwards on one connection; independent and repeated Stop passed.");
    }

    [Fact]
    public async Task LocalOccupiedPortHasStructuredSocketErrorAndIsReusableAfterRelease()
    {
        using TestConnection connection = fixture.CreateConnection();
        await connection.Client.ConnectAsync(CancellationToken.None);
        var occupied = new TcpListener(IPAddress.Loopback, 0);
        occupied.Server.ExclusiveAddressUse = true;
        occupied.Start();
        int requestedPort = ((IPEndPoint)occupied.LocalEndpoint).Port;
        try
        {
            using var failed = new ForwardedPortLocal("127.0.0.1", (uint)requestedPort, "127.0.0.1", (uint)fixture.Manifest.EchoPort);
            connection.Client.AddForwardedPort(failed);
            SocketException error = Assert.Throws<SocketException>(failed.Start);
            Assert.Contains(error.SocketErrorCode, new[] { SocketError.AddressAlreadyInUse, SocketError.AccessDenied });
            output.WriteLine($"SSH-02 local: {error.GetType().Name}, SocketError={error.SocketErrorCode}, NativeError={error.NativeErrorCode}. AccessDenied must not trigger automatic port search.");
            Assert.False(failed.IsStarted);
            connection.Client.RemoveForwardedPort(failed);
        }
        finally
        {
            occupied.Stop();
        }
        using var retry = new ForwardedPortLocal("127.0.0.1", (uint)requestedPort, "127.0.0.1", (uint)fixture.Manifest.EchoPort);
        connection.Client.AddForwardedPort(retry);
        retry.Start();
        await LocalEchoServer.AssertPayloadAsync(requestedPort, [0, 1, 2, 255]);
        retry.Stop();
    }

    [Fact]
    public async Task RemoteOccupiedAndPolicyDeniedHaveTheSameUnstructuredExceptionType()
    {
        int occupiedPort = await fixture.ReservePortAsync();
        try
        {
            Type occupiedType = await AssertRemoteRefusalAsync(fixture.Manifest.AllowPort, occupiedPort, "occupied");
            Type disabledType = await AssertRemoteRefusalAsync(fixture.Manifest.DenyPort, await fixture.UnusedPortAsync(), "AllowTcpForwarding=no");
            Type restrictedType = await AssertRemoteRefusalAsync(fixture.Manifest.RestrictedPort, await fixture.UnusedPortAsync(), "PermitListen restriction");
            Assert.Equal(typeof(SshException), occupiedType);
            Assert.Equal(occupiedType, disabledType);
            Assert.Equal(occupiedType, restrictedType);
        }
        finally
        {
            await fixture.ReleasePortAsync(occupiedPort);
        }

        using TestConnection connection = fixture.CreateConnection();
        await connection.Client.ConnectAsync(CancellationToken.None);
        await using var echo = new LocalEchoServer();
        using var retry = new ForwardedPortRemote(IPAddress.Loopback, (uint)occupiedPort, IPAddress.Loopback, (uint)echo.Port);
        connection.Client.AddForwardedPort(retry);
        retry.Start();
        await fixture.AssertRemotePayloadAsync(occupiedPort, [255, 0, 127]);
        retry.Stop();
        await fixture.ExpectListeningAsync(occupiedPort, false);
    }

    [Fact]
    public async Task PermitListenAllowsItsExplicitPort()
    {
        using TestConnection connection = fixture.CreateConnection(fixture.Manifest.RestrictedPort);
        await connection.Client.ConnectAsync(CancellationToken.None);
        await using var echo = new LocalEchoServer();
        using var forward = new ForwardedPortRemote(IPAddress.Loopback, (uint)fixture.Manifest.PermitListenPort, IPAddress.Loopback, (uint)echo.Port);
        connection.Client.AddForwardedPort(forward);
        forward.Start();
        await fixture.AssertRemotePayloadAsync(fixture.Manifest.PermitListenPort, [4, 3, 2, 1]);
        forward.Stop();
    }

    [Fact]
    public async Task ExplicitIpv6LoopbackWorksInBothDirections()
    {
        using TestConnection connection = fixture.CreateConnection();
        await connection.Client.ConnectAsync(CancellationToken.None);
        await using var echo = new LocalEchoServer(IPAddress.IPv6Loopback);
        int remotePort = await fixture.UnusedPortAsync();
        using var local = new ForwardedPortLocal("::1", 0, "::1", (uint)fixture.Manifest.EchoV6Port);
        using var remote = new ForwardedPortRemote(IPAddress.IPv6Loopback, (uint)remotePort, IPAddress.IPv6Loopback, (uint)echo.Port);
        connection.Client.AddForwardedPort(local);
        connection.Client.AddForwardedPort(remote);
        local.Start();
        remote.Start();
        await LocalEchoServer.AssertPayloadAsync((int)local.BoundPort, [6, 0, 6], IPAddress.IPv6Loopback);
        await fixture.AssertRemotePayloadAsync(remotePort, [6, 1, 6], "::1");
        local.Stop();
        remote.Stop();
        await fixture.ExpectListeningAsync(remotePort, false, "::1");
        output.WriteLine("SSH address boundary: explicit IPv4/IPv6 loopback passed; string Remote constructor resolves bind and target on the client. Use numeric bind addresses and explicit client-side target resolution.");
    }

    [Fact]
    public async Task UnreachableTargetDoesNotTurnListenerIntoASessionFailure()
    {
        using TestConnection connection = fixture.CreateConnection();
        await connection.Client.ConnectAsync(CancellationToken.None);
        int closedTarget = await fixture.UnusedPortAsync();
        using var local = new ForwardedPortLocal("127.0.0.1", 0, "127.0.0.1", (uint)closedTarget);
        var requestError = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        local.Exception += (_, args) => requestError.TrySetResult(args.Exception);
        connection.Client.AddForwardedPort(local);
        local.Start();
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, (int)local.BoundPort);
        byte[] buffer = new byte[1];
        int count = await client.GetStream().ReadAsync(buffer).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, count);
        Assert.True(local.IsStarted);
        Assert.True(connection.Client.IsConnected);
        output.WriteLine($"Target refused: consumer stream reached EOF; listener and SSH connection remain started. ForwardedPort.Exception event observed = {requestError.Task.IsCompletedSuccessfully}. No target-health claim is made.");
        local.Stop();
    }

    private async Task<Type> AssertRemoteRefusalAsync(int serverPort, int listenPort, string scenario)
    {
        using TestConnection connection = fixture.CreateConnection(serverPort);
        await connection.Client.ConnectAsync(CancellationToken.None);
        using var port = new ForwardedPortRemote(IPAddress.Loopback, (uint)listenPort, IPAddress.Loopback, 9);
        connection.Client.AddForwardedPort(port);
        SshException error = Assert.Throws<SshException>(port.Start);
        Assert.False(port.IsStarted);
        Assert.True(connection.Client.IsConnected);
        output.WriteLine($"SSH-03 {scenario}: {error.GetType().FullName}; HResult={error.HResult}; listener not started. Message is diagnostic only, never a classifier.");
        return error.GetType();
    }
}

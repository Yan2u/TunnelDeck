using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tunnels;
using TunnelDeck.Ssh.Tests.Fixture;
using Xunit.Abstractions;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class EngineForwardingTests(SshFixture fixture, ITestOutputHelper output)
{
    [Theory]
    [InlineData(ForwardMode.Local)]
    [InlineData(ForwardMode.Remote)]
    public async Task LargePayloadAndConcurrentClientsTraverseTheActualRequestedDirection(ForwardMode mode)
    {
        using var block = LocalPortBlock.Reserve(1);
        block.Release(0);
        await using var echo = new LocalEchoServer();
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        int remotePort = mode == ForwardMode.Local ? fixture.Manifest.EchoPort : await fixture.UnusedPortAsync();
        var request = scope.Rule(mode == ForwardMode.Local ? block.First : echo.Port, remotePort, mode);
        TunnelStartResult result = await scope.Tunnels.StartAsync(request);
        Assert.True(result.Succeeded, result.Tunnel.LastError?.Message);
        Assert.Equal(request.Requested.GetRoute(), result.Tunnel.Actual);
        byte[] large = RandomNumberGenerator.GetBytes(1024 * 1024);
        if (mode == ForwardMode.Local) { await LocalEchoServer.AssertPayloadAsync(block.First, large); }
        else { await fixture.AssertRemotePayloadAsync(remotePort, large); }
        byte[] parallel = RandomNumberGenerator.GetBytes(65536);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => mode == ForwardMode.Local
            ? LocalEchoServer.AssertPayloadAsync(block.First, parallel) : fixture.AssertRemotePayloadAsync(remotePort, parallel)));
        Assert.Equal(1, scope.Tunnels.Current.RunningCount);
        Assert.True((await scope.Tunnels.StopAsync(result.Tunnel.Id)).Succeeded);
        if (mode == ForwardMode.Local) { LocalPortBlock.AssertReusable(block.First); }
        else { await fixture.ExpectListeningAsync(remotePort, false); }
    }

    [Fact]
    public async Task HttpWorksThroughBothModesWithoutChangingItsContent()
    {
        using var block = LocalPortBlock.Reserve(1);
        block.Release(0);
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        // The isolated control server's public /health handler is reachable on 8080 inside the container.
        TunnelStartResult local = await scope.Tunnels.StartAsync(scope.Rule(block.First, 8080));
        Assert.True(local.Succeeded, local.Tunnel.LastError?.Message);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromSeconds(5) };
        using JsonDocument health = JsonDocument.Parse(await http.GetStringAsync($"http://127.0.0.1:{block.First}/health"));
        Assert.True(health.RootElement.GetProperty("ready").GetBoolean());

        byte[] body = RandomNumberGenerator.GetBytes(128 * 1024);
        await using var server = new LocalHttpServer(body);
        int remotePort = await fixture.UnusedPortAsync();
        TunnelStartResult remote = await scope.Tunnels.StartAsync(scope.Rule(server.Port, remotePort, ForwardMode.Remote));
        Assert.True(remote.Succeeded, remote.Tunnel.LastError?.Message);
        JsonElement response = await fixture.PostAsync("probe", new { port = remotePort, payload = Convert.ToBase64String(LocalHttpServer.Request), expectedBytes = server.Response.Length });
        Assert.Equal(server.Response, Convert.FromBase64String(response.GetProperty("payload").GetString()!));
    }

    [Fact]
    public async Task DuplicateRulesBindOnceAndStoppingOneLocalDoesNotInterruptAnother()
    {
        using var block = LocalPortBlock.Reserve(2);
        block.Release(0);
        block.Release(1);
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        var request = scope.Rule(block.First, fixture.Manifest.EchoPort);
        TunnelStartResult[] duplicates = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => scope.Tunnels.StartAsync(request)));
        Assert.Single(duplicates.Select(result => result.Tunnel.Id).Distinct());
        Assert.Single(duplicates, result => !result.Reused);
        Assert.Single(scope.Connections.Factory.Sessions);
        TunnelStartResult other = await scope.Tunnels.StartAsync(scope.Rule(block.First + 1, fixture.Manifest.EchoPort));
        Assert.True(other.Succeeded);
        Assert.Single(scope.Connections.Factory.Sessions);
        Assert.True((await scope.Tunnels.StopAsync(duplicates[0].Tunnel.Id)).Succeeded);
        Assert.True((await scope.Tunnels.StopAsync(duplicates[0].Tunnel.Id)).Succeeded);
        await LocalEchoServer.AssertPayloadAsync(other.Tunnel.Actual!.Local.Port, [1, 3, 3, 7]);
        LocalPortBlock.AssertReusable(block.First);
        TunnelStartResult restarted = await scope.Tunnels.StartAsync(request);
        Assert.True(restarted.Succeeded);
        Assert.NotEqual(duplicates[0].Tunnel.Id, restarted.Tunnel.Id);
        Assert.Equal(block.First, restarted.Tunnel.Actual!.Local.Port);
    }

    [Fact]
    public async Task LocalTargetCanBecomeAvailableWithoutRestartingTheListener()
    {
        using var block = LocalPortBlock.Reserve(1);
        block.Release(0);
        int targetPort = await fixture.UnusedPortAsync();
        bool echoStarted = false;
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        try
        {
            TunnelStartResult start = await scope.Tunnels.StartAsync(scope.Rule(block.First, targetPort));
            Assert.True(start.Succeeded);
            using (var client = new TcpClient())
            using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                await client.ConnectAsync(IPAddress.Loopback, block.First, deadline.Token);
                byte[] probe = [1, 2, 3];
                await client.GetStream().WriteAsync(probe, deadline.Token);
                Assert.Equal(0, await client.GetStream().ReadAsync(probe, deadline.Token));
            }
            ActiveTunnel unavailable = scope.Tunnels.Current.Tunnels.Single();
            Assert.Equal(TunnelState.Running, unavailable.State);
            Assert.Null(unavailable.LastError);
            Assert.True(unavailable.AcceptedConnections >= 1);
            // SSH.NET does not surface direct-tcpip OpenFailed through ForwardedPortLocal.Exception.
            // No fabricated target-health or occupation error is inferred from listener success.
            await fixture.PostAsync("start-echo", new { port = targetPort });
            echoStarted = true;
            await LocalEchoServer.AssertPayloadAsync(block.First, [0, 9, 255, 4]);
            Assert.Equal(start.Tunnel.Id, scope.Tunnels.Current.Tunnels.Single().Id);
            Assert.Equal(TunnelState.Running, scope.Tunnels.Current.Tunnels.Single().State);
        }
        finally { if (echoStarted) { await fixture.PostAsync("stop-echo", new { port = targetPort }); } }
    }

    [Fact]
    public async Task RemoteTargetFailureIsReportedAsARequestErrorAndTheListenerCanRecover()
    {
        using var block = LocalPortBlock.Reserve(1);
        block.Release(0);
        int remotePort = await fixture.UnusedPortAsync();
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        TunnelStartResult start = await scope.Tunnels.StartAsync(scope.Rule(block.First, remotePort, ForwardMode.Remote));
        Assert.True(start.Succeeded);
        var noticed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Tunnels.StateChanged += state => { if (state.Tunnels.Single().RequestFailureCount > 0) { noticed.TrySetResult(); } };
        // With buffered client data OpenSSH can leave the failed channel's TCP consumer waiting.
        // Assert the adapter's actual error independently of whether the client sees EOF, reset or timeout.
        JsonElement failed = await fixture.PostAsync("probe", new
        {
            port = remotePort, payload = Convert.ToBase64String(new byte[] { 1, 2, 3 }), observeFailure = true,
        });
        Assert.Empty(Convert.FromBase64String(failed.GetProperty("payload").GetString()!));
        string? outcome = failed.GetProperty("outcome").GetString();
        Assert.Contains(outcome, new[] { "eof", "reset", "timeout" });
        await noticed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        ActiveTunnel current = scope.Tunnels.Current.Tunnels.Single();
        Assert.Equal(TunnelState.Running, current.State);
        Assert.Null(current.LastError);
        Assert.Equal(TunnelFailureKind.ConnectionRequest, current.LastRequestError!.Kind);
        output.WriteLine($"Unavailable Remote target: request diagnostic observed; client outcome = {outcome}; listener remains Running.");
        await using var echo = new LocalEchoServer(port: block.First);
        await fixture.AssertRemotePayloadAsync(remotePort, [0, 9, 255, 4]);
        Assert.Equal(TunnelState.Running, scope.Tunnels.Current.Tunnels.Single().State);
    }
}

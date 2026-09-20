using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tunnels;
using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class EngineAllocationTests(SshFixture fixture)
{
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task RealLocalOccupationUsesTheSmallestAvailablePort(int occupied)
    {
        using var block = LocalPortBlock.Reserve(occupied + 1);
        block.Release(occupied);
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        var request = scope.Rule(block.First, fixture.Manifest.EchoPort);
        TunnelStartResult result = await scope.Tunnels.StartAsync(request);
        Assert.True(result.Succeeded, result.Tunnel.LastError?.Message);
        Assert.Equal(block.First, result.Tunnel.Requested.LocalPort);
        Assert.Equal(block.First + occupied, result.Tunnel.Actual!.Local.Port);
        Assert.Equal(occupied + 1, result.Tunnel.BindAttempts);
        Assert.Equal(PortAdjustmentReason.LocalListenerInUse, result.Tunnel.AdjustmentReason);
        Assert.Equal(request.Requested.RemotePort, result.Tunnel.Actual.Remote.Port);
        await LocalEchoServer.AssertPayloadAsync(result.Tunnel.Actual.Local.Port, [1, 255, 3, 0, 5]);
        Assert.True((await scope.Tunnels.StopAsync(result.Tunnel.Id)).Succeeded);
        LocalPortBlock.AssertReusable(result.Tunnel.Actual.Local.Port);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 20)]
    public async Task DisabledAndExhaustedSearchAreBoundedAndReleaseTheirSession(bool auto, int count)
    {
        using var block = LocalPortBlock.Reserve(count);
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        TunnelStartResult result = await scope.Tunnels.StartAsync(scope.Rule(block.First, fixture.Manifest.EchoPort, auto: auto));
        Assert.Equal(TunnelState.Failed, result.Tunnel.State);
        Assert.Equal(TunnelFailureKind.LocalAddressInUse, result.Tunnel.LastError!.Kind);
        Assert.Equal(count, result.Tunnel.BindAttempts);
        Assert.Equal(auto ? PortSearchStopReason.AttemptLimitReached : PortSearchStopReason.RetryDisabled, result.Tunnel.PortSearchStopReason);
        Assert.Equal(0, scope.Connections.Manager.TrackedConnectionCount);
        Assert.All(scope.Connections.Factory.Sessions, session => Assert.True(session.IsDisposed));
    }

    [Theory]
    [InlineData("occupied")]
    [InlineData("denied")]
    [InlineData("restricted")]
    public async Task EveryRemoteRefusalStopsAfterOneRequestWithoutClaimingOccupation(string scenario)
    {
        int sshPort = scenario switch { "denied" => fixture.Manifest.DenyPort, "restricted" => fixture.Manifest.RestrictedPort, _ => fixture.Manifest.AllowPort };
        int remotePort = scenario == "occupied" ? await fixture.ReservePortAsync() : await fixture.UnusedPortAsync();
        try
        {
            await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest, sshPort);
            await using var echo = new LocalEchoServer();
            var request = scope.Rule(echo.Port, remotePort, ForwardMode.Remote);
            request = request with { Requested = request.Requested with { AutoFindAvailablePort = true } };
            TunnelStartResult result = await scope.Tunnels.StartAsync(request);
            Assert.Equal(TunnelState.Failed, result.Tunnel.State);
            Assert.Equal(TunnelFailureKind.RemoteListenFailure, result.Tunnel.LastError!.Kind);
            Assert.Equal(1, result.Tunnel.BindAttempts);
            Assert.Null(result.Tunnel.AdjustmentReason);
            Assert.DoesNotContain("already in use", result.Tunnel.LastError.Message);
            Assert.Equal(0, scope.Connections.Manager.TrackedConnectionCount);
        }
        finally { if (scenario == "occupied") { await fixture.ReleasePortAsync(remotePort); } }
    }

    [Fact]
    public async Task DifferentServerIdsStillCompeteForTheSameLocalSocket()
    {
        using var block = LocalPortBlock.Reserve(2);
        block.Release(0);
        block.Release(1);
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        RemoteServer alias = scope.Server with { Id = Guid.NewGuid(), DisplayName = "Independent identity", CredentialReference = null };
        await scope.Connections.Configuration.SaveServerAsync(alias, Core.Persistence.CredentialChange.Remember(fixture.Manifest.Password));
        alias = scope.Connections.Configuration.Current!.Servers.Single(server => server.Id == alias.Id);
        var first = scope.Rule(block.First, fixture.Manifest.EchoPort);
        var second = scope.Rule(block.First, fixture.Manifest.EchoPort, server: alias);
        TunnelStartResult[] results = await Task.WhenAll(scope.Tunnels.StartAsync(first), scope.Tunnels.StartAsync(second));
        Assert.All(results, result => Assert.True(result.Succeeded, result.Tunnel.LastError?.Message));
        Assert.Equal(new[] { block.First, block.First + 1 }, results.Select(result => result.Tunnel.Actual!.Local.Port).Order());
        Assert.Equal(1, results.Count(result => result.Tunnel.AdjustmentReason is not null));
        foreach (TunnelStartResult result in results) { await LocalEchoServer.AssertPayloadAsync(result.Tunnel.Actual!.Local.Port, [3, 4, 7, 0]); }
    }
}

using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Snapshots;
using TunnelDeck.Core.Transfer;
using TunnelDeck.Infrastructure.Transfer;
using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class TransferForwardingTests(SshFixture fixture)
{
    [Fact]
    public async Task ExportedCrossServerProfileTransfersAfterImportAndLeavesExistingForwardAlone()
    {
        await using var scope = await TunnelTestScope.CreateAsync(fixture.Manifest);
        await using var echo = new LocalEchoServer();
        using var ports = LocalPortBlock.Reserve(2); ports.Release(0); ports.Release(1);
        var existing = await scope.Tunnels.StartAsync(scope.Rule(ports.First, fixture.Manifest.EchoPort));
        Assert.True(existing.Succeeded);
        int remotePort = await fixture.UnusedPortAsync();
        RemoteServer first = ConnectionTestScope.Server(fixture.Manifest) with
        { AuthMethod = AuthenticationMethod.PrivateKey, IdentityFilePath = fixture.Manifest.PlainPrivateKeyPath };
        RemoteServer second = first with { Id = Guid.NewGuid(), Port = fixture.Manifest.MutablePort };
        ForwardProfile profile = new()
        {
            Id = Guid.NewGuid(), Name = "Portable mixed profile",
            Rules = [scope.Rule(ports.First + 1, fixture.Manifest.EchoPort, server: first).Requested,
                scope.Rule(echo.Port, remotePort, ForwardMode.Remote, server: second).Requested],
        };
        AppState source = new()
        {
            Servers = [first, second], Profiles = [profile],
            Order = new() { PinnedServerIds = [second.Id], OtherServerIds = [first.Id], PinnedProfileIds = [profile.Id] },
        };
        TunnelDeckExport export = ExportJsonCodec.Decode(ExportJsonCodec.Encode(ExportBuilder.Build(source, ExportScope.Profiles, DateTimeOffset.UtcNow)));
        ImportPlan plan = ImportPlanner.Plan(export, scope.Connections.Configuration.Current!, File.Exists);
        await new ResourceImportService(scope.Connections.Configuration, scope.Connections.Manager.Work, File.Exists).CommitAsync(plan);
        AppState imported = scope.Connections.Configuration.Current!;
        ProfileLaunchSnapshot launch = ProfileSnapshotFactory.Capture(Assert.Single(plan.Profiles), imported.Servers);
        var started = await scope.Tunnels.StartAllAsync(launch.Rules);
        Assert.All(started, result => Assert.True(result.Succeeded, result.Failure?.Message));
        Assert.All(plan.Servers, server => Assert.Null(server.CredentialReference));
        Assert.Equal(3, scope.Tunnels.Current.Tunnels.Count(tunnel => tunnel.IsRunning));
        Assert.Equal(TunnelState.Running, scope.Tunnels.Current.Tunnels.Single(tunnel => tunnel.Id == existing.Tunnel.Id).State);
        await LocalEchoServer.AssertPayloadAsync(ports.First, [1, 9, 0, 255]);
        await LocalEchoServer.AssertPayloadAsync(ports.First + 1, [2, 9, 0, 255]);
        await fixture.AssertRemotePayloadAsync(remotePort, [3, 9, 0, 255]);
        Assert.DoesNotContain(launch.Rules, rule => source.Servers.Any(server => server.Id == rule.Server.Id));
    }

    [Fact]
    public async Task ImportDuringBackoffCannotReviveADeletedServerAndNewIdentityCanStartNormally()
    {
        await using var proxy = new MultiConnectionSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        await using var scope = await RecoveryTestScope.CreateAsync(fixture.Manifest, proxy.Port);
        using var ports = LocalPortBlock.Reserve(1); ports.Release(0);
        var started = await scope.Tunnels.StartAsync(scope.Backend.Rule(ports.First, fixture.Manifest.EchoPort));
        Assert.True(started.Succeeded);
        await scope.Activity.FlushAsync();
        proxy.RejectConnections(true); proxy.DisconnectAll();
        await scope.WaitAsync(() => scope.Tunnel(started.Tunnel.Id).NextReconnectAt is not null);
        TunnelDeckExport export = ExportBuilder.Build(scope.Connections.Configuration.Current!, ExportScope.Servers, DateTimeOffset.UtcNow);
        export = export with { Servers = [export.Servers[0] with
        { AuthMethod = AuthenticationMethod.PrivateKey, IdentityFilePath = fixture.Manifest.PlainPrivateKeyPath }] };
        ImportPlan plan = ImportPlanner.Plan(export, scope.Connections.Configuration.Current!, File.Exists);
        await new ResourceImportService(scope.Connections.Configuration, scope.Connections.Manager.Work, File.Exists).CommitAsync(plan);
        Assert.Equal(1, scope.Reconnect.ActiveServerCount);
        Assert.Equal(started.Tunnel.ServerId, scope.Tunnel(started.Tunnel.Id).ServerId);
        var deleted = await scope.Deletion.DeleteConfirmedAsync(scope.Deletion.CalcServerDeletionImpact([started.Tunnel.ServerId]));
        Assert.True(deleted.Succeeded, deleted.Message);
        await scope.WaitAsync(() => scope.Reconnect.ActiveServerCount == 0);
        proxy.RejectConnections(false);
        Assert.DoesNotContain(scope.Connections.Configuration.Current!.Servers, server => server.Id == started.Tunnel.ServerId);
        Assert.DoesNotContain(scope.Tunnels.Current.Tunnels, tunnel => tunnel.IsRunning);
        RemoteServer imported = Assert.Single(plan.Servers);
        var restored = await scope.Tunnels.StartAsync(scope.Backend.Rule(ports.First, fixture.Manifest.EchoPort, server: imported));
        Assert.True(restored.Succeeded, restored.Tunnel.LastError?.Message);
        Assert.NotEqual(started.Tunnel.Id, restored.Tunnel.Id);
        Assert.Equal(imported.Id, restored.Tunnel.ServerId);
        await LocalEchoServer.AssertPayloadAsync(ports.First, [7, 11, 0, 255]);
    }
}

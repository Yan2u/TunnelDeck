using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class DeletionForwardingTests(SshFixture fixture)
{
    [Fact]
    public async Task ProfileDeletionPreservesRealPayloadAndServerDeletionThenReleasesBothListeners()
    {
        using var ports = LocalPortBlock.Reserve(1); ports.Release(0);
        await using var echo = new LocalEchoServer();
        await using var scope = await ProfileTestScope.CreateAsync(fixture.Manifest);
        int remotePort = await fixture.UnusedPortAsync();
        await scope.SaveRulesAsync(scope.Backend.Rule(ports.First, fixture.Manifest.EchoPort).Requested,
            scope.Backend.Rule(echo.Port, remotePort, ForwardMode.Remote).Requested);
        Assert.Equal(2, (await scope.Runner.StartAllAsync(scope.Profile.Id)).StartedCount);
        await scope.Activity.FlushAsync();
        var deletion = new ResourceDeletionService(scope.Backend.Connections.Configuration, scope.Backend.Connections.Manager,
            scope.Backend.Tunnels, scope.Activity);
        var profile = await deletion.DeleteConfirmedAsync(deletion.CalcProfileDeletionImpact([scope.Profile.Id]));
        Assert.True(profile.Succeeded);
        await LocalEchoServer.AssertPayloadAsync(ports.First, [3, 1, 4, 1, 5]);
        await fixture.AssertRemotePayloadAsync(remotePort, [2, 7, 1, 8, 2]);
        Assert.All(scope.Backend.Tunnels.Current.Tunnels, tunnel => Assert.Equal(scope.Profile.Name, tunnel.Source.ProfileName));
        var servers = await deletion.DeleteConfirmedAsync(deletion.CalcAllDeletionImpact(ResourceKind.Server));
        Assert.True(servers.Succeeded); Assert.Equal(2, servers.StoppedTunnelIds.Length);
        LocalPortBlock.AssertReusable(ports.First);
        await fixture.ExpectListeningAsync(remotePort, false);
        Assert.Empty(scope.Backend.Connections.Configuration.Current!.Servers);
        Assert.Equal(0, scope.Backend.Connections.Manager.TrackedConnectionCount);
    }

    [Fact]
    public async Task DeletingOneServerKeepsTheOtherServersCrossProfilePayloadAndConfiguration()
    {
        using var ports = LocalPortBlock.Reserve(1); ports.Release(0);
        await using var echo = new LocalEchoServer();
        await using var scope = await ProfileTestScope.CreateAsync(fixture.Manifest);
        var second = await scope.AddServerAsync(fixture.Manifest, fixture.Manifest.AllowPort);
        int remotePort = await fixture.UnusedPortAsync();
        var firstRule = scope.Backend.Rule(ports.First, fixture.Manifest.EchoPort).Requested;
        await scope.SaveRulesAsync(firstRule, scope.Backend.Rule(echo.Port, remotePort, ForwardMode.Remote, server: second).Requested);
        Assert.Equal(2, (await scope.Runner.StartAllAsync(scope.Profile.Id)).StartedCount);
        await scope.Activity.FlushAsync();
        var deletion = new ResourceDeletionService(scope.Backend.Connections.Configuration, scope.Backend.Connections.Manager,
            scope.Backend.Tunnels, scope.Activity);
        var result = await deletion.DeleteConfirmedAsync(deletion.CalcServerDeletionImpact([firstRule.ServerId]));
        Assert.True(result.Succeeded);
        LocalPortBlock.AssertReusable(ports.First);
        await fixture.AssertRemotePayloadAsync(remotePort, [0, 255, 9, 8, 7]);
        var state = scope.Backend.Connections.Configuration.Current!;
        Assert.Equal(second.Id, Assert.Single(state.Servers).Id);
        Assert.Equal(second.Id, Assert.Single(Assert.Single(state.Profiles).Rules).ServerId);
        Assert.Single(scope.Backend.Tunnels.Current.Tunnels.Where(tunnel => tunnel.State == TunnelState.Running));
    }

    [Fact]
    public async Task DeletionDrainsAnUnacknowledgedRealRemoteListenerAndRetainsOtherServerTraffic()
    {
        using var ports = LocalPortBlock.Reserve(1); ports.Release(0);
        await using var echo = new LocalEchoServer();
        await using var proxy = new MultiConnectionSshProxy(fixture.Manifest.Host, fixture.Manifest.AllowPort);
        await using var scope = await ProfileTestScope.CreateAsync(fixture.Manifest);
        var second = await scope.AddServerAsync(fixture.Manifest, proxy.Port);
        scope.Backend.Connections.Factory.SessionOpened = session => { if (session.ServerId == second.Id) { proxy.HoldNewestReplies(); } };
        int remotePort = await fixture.UnusedPortAsync();
        await scope.SaveRulesAsync(scope.Backend.Rule(ports.First, fixture.Manifest.EchoPort).Requested,
            scope.Backend.Rule(echo.Port, remotePort, ForwardMode.Remote, server: second).Requested);
        var localStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        scope.Runner.StateChanged += run => { if (run.StartedCount > 0) { localStarted.TrySetResult(); } };
        var start = scope.Runner.StartAllAsync(scope.Profile.Id);
        await Task.WhenAll(localStarted.Task, proxy.WaitForHeldReplyAsync()).WaitAsync(TimeSpan.FromSeconds(10));
        await fixture.ExpectListeningAsync(remotePort, true);
        var deletion = new ResourceDeletionService(scope.Backend.Connections.Configuration, scope.Backend.Connections.Manager,
            scope.Backend.Tunnels, scope.Activity);
        var result = await deletion.DeleteConfirmedAsync(deletion.CalcServerDeletionImpact([second.Id])).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(result.Succeeded);
        Assert.Equal(1, (await start).CancelledCount);
        await fixture.ExpectListeningAsync(remotePort, false);
        await LocalEchoServer.AssertPayloadAsync(ports.First, [8, 9, 255, 0]);
        Assert.DoesNotContain(scope.Backend.Tunnels.Current.Tunnels, tunnel => tunnel.State is TunnelState.Starting or TunnelState.Stopping);
    }
}

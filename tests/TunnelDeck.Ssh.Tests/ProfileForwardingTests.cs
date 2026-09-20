using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Profiles;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Ssh.Tests.Fixture;

namespace TunnelDeck.Ssh.Tests;

[Collection(SshCollection.Name)]
public sealed class ProfileForwardingTests(SshFixture fixture)
{
    [Fact]
    public async Task CrossServerProfileTransfersThroughLocalAndRemoteAndRepeatedStartIsIdempotent()
    {
        using var ports = LocalPortBlock.Reserve(2);
        ports.Release(0); ports.Release(1);
        await using var echo = new LocalEchoServer();
        await using var scope = await ProfileTestScope.CreateAsync(fixture.Manifest);
        var second = await scope.AddServerAsync(fixture.Manifest, fixture.Manifest.AllowPort);
        int remotePort = await fixture.UnusedPortAsync();
        await scope.SaveRulesAsync(scope.Backend.Rule(ports.First, fixture.Manifest.EchoPort).Requested,
            scope.Backend.Rule(ports.First + 1, fixture.Manifest.EchoPort).Requested,
            scope.Backend.Rule(echo.Port, remotePort, ForwardMode.Remote, server: second).Requested);
        ProfileRunSnapshot run = await scope.Runner.StartAllAsync(scope.Profile.Id);
        Assert.Equal(3, run.StartedCount);
        await LocalEchoServer.AssertPayloadAsync(ports.First, [0, 1, 255, 8]);
        await LocalEchoServer.AssertPayloadAsync(ports.First + 1, [3, 4, 9]);
        await fixture.AssertRemotePayloadAsync(remotePort, [8, 6, 7, 5, 3, 0, 9]);
        Guid[] ids = run.Rules.Select(rule => rule.TunnelId!.Value).ToArray();
        Assert.Equal(3, (await scope.Runner.StartAllAsync(scope.Profile.Id)).AlreadyRunningCount);
        Assert.Equal(ids.Order(), scope.Backend.Tunnels.Current.Tunnels.Select(tunnel => tunnel.Id).Order());
        Assert.True(await scope.Activity.FlushAsync());
        var stats = scope.Backend.Connections.Configuration.Current!.Statistics;
        Assert.Equal(3, stats.TotalForwards); Assert.Equal(1, stats.ProfileUses[scope.Profile.Id]);
        Assert.True((await scope.Backend.Tunnels.StopAsync(ids[0])).Succeeded);
        LocalPortBlock.AssertReusable(ports.First);
        await LocalEchoServer.AssertPayloadAsync(ports.First + 1, [4, 2]);
        await fixture.AssertRemotePayloadAsync(remotePort, [4, 2]);
    }

    [Fact]
    public async Task ARefusedServerLeavesSuccessfulRulesRunningAndRetryOnlyStartsItsFailedRule()
    {
        using var ports = LocalPortBlock.Reserve(1); ports.Release(0);
        await using var echo = new LocalEchoServer();
        await using var scope = await ProfileTestScope.CreateAsync(fixture.Manifest);
        RemoteServer second = await scope.AddServerAsync(fixture.Manifest, fixture.Manifest.DenyPort);
        int remotePort = await fixture.UnusedPortAsync();
        await scope.SaveRulesAsync(scope.Backend.Rule(ports.First, fixture.Manifest.EchoPort).Requested,
            scope.Backend.Rule(echo.Port, remotePort, ForwardMode.Remote, server: second).Requested);
        var partial = await scope.Runner.StartAllAsync(scope.Profile.Id);
        Assert.Equal(1, partial.StartedCount); Assert.Equal(1, partial.FailedCount);
        Assert.Equal(TunnelFailureKind.RemoteListenFailure, partial.Rules[1].Failure!.Kind);
        await LocalEchoServer.AssertPayloadAsync(ports.First, [1, 4, 9]);
        await scope.Backend.Connections.Configuration.SaveServerAsync(second with { Port = fixture.Manifest.AllowPort }, CredentialChange.Keep);
        var retried = await scope.Runner.RetryFailedAsync(scope.Profile.Id);
        Assert.Equal(0, retried.FailedCount); Assert.Equal(2, retried.StartedCount);
        Assert.Equal(partial.Rules[0].TunnelId, retried.Rules[0].TunnelId);
        await LocalEchoServer.AssertPayloadAsync(ports.First, [2, 7, 1, 8]);
        await fixture.AssertRemotePayloadAsync(remotePort, [3, 1, 4, 1, 5]);
        Assert.True(await scope.Activity.FlushAsync());
        Assert.Equal(2, scope.Backend.Connections.Configuration.Current!.Statistics.TotalForwards);
    }

    [Fact]
    public async Task CancelWithARealRemoteAcknowledgementPendingReleasesItAndKeepsTheCompletedLocalRule()
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
        await Task.WhenAll(proxy.WaitForHeldReplyAsync(), localStarted.Task).WaitAsync(TimeSpan.FromSeconds(10));
        await fixture.ExpectListeningAsync(remotePort, true);
        await scope.Runner.CancelAsync(scope.Profile.Id).WaitAsync(TimeSpan.FromSeconds(10));
        var result = await start;
        Assert.Equal(1, result.StartedCount); Assert.Equal(1, result.CancelledCount);
        await fixture.ExpectListeningAsync(remotePort, false);
        await LocalEchoServer.AssertPayloadAsync(ports.First, [0, 8, 16, 255]);
        Assert.DoesNotContain(scope.Backend.Tunnels.Current.Tunnels, tunnel => tunnel.State is TunnelState.Starting or TunnelState.Stopping);
    }
}

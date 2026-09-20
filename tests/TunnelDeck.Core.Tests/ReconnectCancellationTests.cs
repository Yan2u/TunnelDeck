using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class ReconnectCancellationTests
{
    [Fact]
    public async Task ARecoveryDeadlineRetainsALateConnectUntilItsSessionHasBeenReleased()
    {
        await using var fixture = await ReconnectFixture.CreateAsync(TimeSpan.FromSeconds(5));
        using var connecting = new AsyncGate();
        var started = await fixture.Backend.Manager.StartAsync(fixture.Backend.Launch(mode: ForwardMode.Remote));
        fixture.Backend.Connections.Factory.BeforeConnect = (_, _) => connecting.WaitAsync();
        fixture.Backend.Connections.Factory.Sessions.Single().Disconnect();
        await fixture.NextRoundAsync();
        await connecting.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fixture.Clock.Advance(TimeSpan.FromSeconds(5));
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(started.Tunnel.Id).State == TunnelState.Stopping);
        Assert.Equal(1, fixture.Reconnect.ActiveServerCount);
        Assert.Single(fixture.Backend.Connections.Manager.Work.Current.Work);
        connecting.Release.TrySetResult();
        await ReconnectFixture.UntilAsync(() => fixture.Reconnect.ActiveServerCount == 0);
        Assert.Equal(TunnelState.Failed, fixture.Tunnel(started.Tunnel.Id).State);
        Assert.Contains("time limit", fixture.Tunnel(started.Tunnel.Id).LastError!.Message);
        Assert.All(fixture.Backend.Connections.Factory.Sessions, session => Assert.True(session.Disposed.Task.IsCompleted));
        Assert.Single(fixture.Backend.Attempts);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("delete")]
    [InlineData("shutdown")]
    public async Task StopDeleteOrExitDuringBackoffCannotReviveAListener(string action)
    {
        await using var fixture = await ReconnectFixture.CreateAsync();
        var started = await fixture.Backend.Manager.StartAsync(fixture.Backend.Launch());
        fixture.Backend.Connections.Factory.Sessions.Single().Disconnect();
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(started.Tunnel.Id).NextReconnectAt is not null);
        if (action == "stop") { Assert.True((await fixture.Backend.Manager.StopAsync(started.Tunnel.Id)).Succeeded); }
        else if (action == "shutdown") { await fixture.Reconnect.ShutdownAsync(CancellationToken.None); }
        else
        {
            var impact = fixture.Deletion.CalcServerDeletionImpact([started.Tunnel.ServerId]);
            Assert.Equal(0, impact.PendingForwardCount); Assert.Single(impact.Tunnels);
            Assert.True((await fixture.Deletion.DeleteConfirmedAsync(impact)).Succeeded);
        }
        await ReconnectFixture.UntilAsync(() => fixture.Reconnect.ActiveServerCount == 0);
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Single(fixture.Backend.Attempts);
        Assert.DoesNotContain(fixture.Backend.Manager.Current.Tunnels, tunnel => DeletionImpactCalculator.IsLive(tunnel));
        Assert.Empty(fixture.Backend.Connections.Manager.Work.Current.Work);
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("delete")]
    [InlineData("shutdown")]
    public async Task LateConnectRemainsOwnedUntilCancelledRecoveryHasActuallyDrained(string action)
    {
        await using var fixture = await ReconnectFixture.CreateAsync();
        using var connecting = new AsyncGate();
        var started = await fixture.Backend.Manager.StartAsync(fixture.Backend.Launch(mode: ForwardMode.Remote));
        fixture.Backend.Connections.Factory.BeforeConnect = (_, _) => connecting.WaitAsync();
        fixture.Backend.Connections.Factory.Sessions.Single().Disconnect();
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(started.Tunnel.Id).NextReconnectAt is not null);
        await fixture.NextRoundAsync();
        await connecting.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task stopping = action switch
        {
            "stop" => fixture.Backend.Manager.StopAsync(started.Tunnel.Id),
            "delete" => fixture.Deletion.DeleteConfirmedAsync(fixture.Deletion.CalcServerDeletionImpact([started.Tunnel.ServerId])),
            _ => fixture.Reconnect.ShutdownAsync(CancellationToken.None),
        };
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(started.Tunnel.Id).StopRequested);
        Assert.False(stopping.IsCompleted);
        connecting.Release.TrySetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        await ReconnectFixture.UntilAsync(() => fixture.Reconnect.ActiveServerCount == 0);
        Assert.All(fixture.Backend.Connections.Factory.Sessions, session => Assert.True(session.Disposed.Task.IsCompleted));
        Assert.Single(fixture.Backend.Attempts);
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.DoesNotContain(fixture.Backend.Manager.Current.Tunnels, tunnel => DeletionImpactCalculator.IsLive(tunnel));
    }

    [Fact]
    public async Task StoppingOneSharedRuleDoesNotCancelAnotherRulesRecovery()
    {
        await using var fixture = await ReconnectFixture.CreateAsync();
        var first = await fixture.Backend.Manager.StartAsync(fixture.Backend.Launch(port: 3200));
        var second = await fixture.Backend.Manager.StartAsync(fixture.Backend.Launch(2, port: 3300));
        fixture.Backend.Connections.Factory.Sessions.Single().Disconnect();
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(second.Tunnel.Id).NextReconnectAt is not null);
        await fixture.Backend.Manager.StopAsync(first.Tunnel.Id);
        await fixture.NextRoundAsync();
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(second.Tunnel.Id).IsRunning && fixture.Reconnect.ActiveServerCount == 0);
        Assert.Equal(TunnelState.Stopped, fixture.Tunnel(first.Tunnel.Id).State);
        Assert.Equal(3, fixture.Backend.Attempts.Count);
    }

    [Fact]
    public async Task ProfileDeletionAndDuplicateStartsKeepTheRecoveringInstanceAndItsSourceSnapshot()
    {
        await using var fixture = await ReconnectFixture.CreateAsync();
        var launch = fixture.Backend.Launch();
        var profile = DomainData.Profile(1, launch.Requested);
        await fixture.Backend.Connections.Configuration.SaveProfileAsync(profile, null);
        launch = launch with { Source = launch.Source with { ProfileId = profile.Id, ProfileName = profile.Name } };
        var started = await fixture.Backend.Manager.StartAsync(launch);
        fixture.Backend.Connections.Factory.Sessions.Single().Disconnect();
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(started.Tunnel.Id).NextReconnectAt is not null);
        var duplicate = await fixture.Backend.Manager.StartAsync(launch);
        Assert.True(duplicate.Reused); Assert.Equal(started.Tunnel.Id, duplicate.Tunnel.Id);
        Assert.Equal(TunnelState.Reconnecting, duplicate.Tunnel.State);
        Assert.True((await fixture.Deletion.DeleteConfirmedAsync(fixture.Deletion.CalcProfileDeletionImpact([profile.Id]))).Succeeded);
        await fixture.NextRoundAsync();
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(started.Tunnel.Id).IsRunning);
        var item = new TunnelItem(fixture.Tunnel(started.Tunnel.Id), fixture.Backend.Connections.Configuration.Current!, fixture.Clock.GetUtcNow());
        Assert.Equal(profile.Name, item.Source); Assert.False(item.CanOpenProfile); Assert.True(item.CanOpenServer);
        Assert.Equal(2, fixture.Backend.Attempts.Count);
    }
}

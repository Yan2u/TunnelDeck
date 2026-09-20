using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Tests;

public sealed class ReconnectRecoveryTests
{
    [Fact]
    public async Task SharedLocalRecoveryKeepsIdsSourcesCountersAndOneTransportWithoutCountingNewStarts()
    {
        await using var fixture = await ReconnectFixture.CreateAsync();
        var backend = fixture.Backend;
        var first = await backend.Manager.StartAsync(backend.Launch(port: 3200));
        var second = await backend.Manager.StartAsync(backend.Launch(2, port: 3300));
        var oldHandles = backend.Handles.ToArray();
        oldHandles[0].Report(new() { AcceptedConnections = 3, FailedConnections = 1 });
        await fixture.Activity.FlushAsync();
        var original = fixture.Tunnel(first.Tunnel.Id);
        Assert.Single(backend.Connections.Factory.Sessions).Disconnect();
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(first.Tunnel.Id).NextReconnectAt is not null && fixture.Tunnel(second.Tunnel.Id).NextReconnectAt is not null);
        Assert.Equal(1, fixture.Reconnect.ActiveServerCount);
        await fixture.NextRoundAsync();
        await ReconnectFixture.UntilAsync(() => backend.Manager.Current.RunningCount == 2 && fixture.Reconnect.ActiveServerCount == 0);
        var recovered = fixture.Tunnel(first.Tunnel.Id);
        Assert.Equal(original.Requested, recovered.Requested); Assert.Equal(original.Source, recovered.Source);
        Assert.Equal(original.StartedAt, recovered.StartedAt); Assert.True(recovered.LastConnectionAt > recovered.StartedAt);
        Assert.Equal(2, recovered.Generation); Assert.Equal(1, recovered.ReconnectCount); Assert.Null(recovered.EndedAt);
        Assert.Equal(2, backend.Connections.Factory.Sessions.Count);
        Assert.All(oldHandles, handle => Assert.True(handle.Disposed));
        Assert.Equal(3, backend.Manager.Current.Tunnels.Sum(tunnel => tunnel.AcceptedConnections));
        Assert.Equal(1, backend.Manager.Current.Tunnels.Sum(tunnel => tunnel.RequestFailureCount));
        await fixture.Activity.FlushAsync();
        Assert.Equal(2, backend.Connections.Configuration.Current!.Statistics.TotalForwards);
        Assert.All(backend.Connections.Configuration.Current.RecentActivity, entry => Assert.Equal(ActivityOutcome.Started, entry.Outcome));
        Assert.Empty(backend.Connections.Manager.Work.Current.Work);
    }

    [Fact]
    public async Task RecoveryTriesTheLastActualLocalPortAndAManualNewStartUsesRequestedAgain()
    {
        await using var fixture = await ReconnectFixture.CreateAsync();
        bool recovering = false;
        fixture.Backend.Start = (session, rule, _) => rule.LocalPort == 3000 || recovering && rule.LocalPort == 3001
            ? throw new ForwardException(new(TunnelFailureKind.LocalAddressInUse, "Reserved by the test."))
            : Task.FromResult<TunnelDeck.Core.Abstractions.IForwardHandle>(fixture.Backend.CreateHandle(session, rule));
        var launch = fixture.Backend.Launch();
        var started = await fixture.Backend.Manager.StartAsync(launch);
        Assert.Equal(3001, started.Tunnel.Actual!.Local.Port);
        recovering = true;
        fixture.Backend.Connections.Factory.Sessions.Single().Disconnect();
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(started.Tunnel.Id).NextReconnectAt is not null);
        await fixture.NextRoundAsync();
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(started.Tunnel.Id).IsRunning);
        Assert.Equal(new[] { 3000, 3001, 3001, 3002 }, fixture.Backend.Attempts.Select(rule => rule.LocalPort));
        Assert.Equal(3000, fixture.Tunnel(started.Tunnel.Id).Requested.LocalPort);
        Assert.Equal(3002, fixture.Tunnel(started.Tunnel.Id).Actual!.Local.Port);
        await fixture.Backend.Manager.RemoveAsync(started.Tunnel.Id);
        fixture.Backend.Start = null;
        var manual = await fixture.Backend.Manager.StartAsync(launch);
        Assert.NotEqual(started.Tunnel.Id, manual.Tunnel.Id); Assert.Equal(3000, manual.Tunnel.Actual!.Local.Port);
    }

    [Theory]
    [InlineData("credentials", TunnelFailureKind.Credentials)]
    [InlineData("host-key", TunnelFailureKind.ChangedHostKey)]
    [InlineData("trust-store", TunnelFailureKind.Storage)]
    [InlineData("remote-refusal", TunnelFailureKind.RemoteListenFailure)]
    public async Task PermanentFailuresDoNotLoopOrPromptForTrust(string fault, TunnelFailureKind expected)
    {
        await using var fixture = await ReconnectFixture.CreateAsync();
        var backend = fixture.Backend;
        var started = await backend.Manager.StartAsync(backend.Launch(mode: ForwardMode.Remote));
        int approvals = backend.Connections.Approval.Requests.Count;
        switch (fault)
        {
            case "credentials": backend.Connections.Credentials.Values.Clear(); break;
            case "host-key": backend.Connections.Factory.KeyFor = server => ConnectionFixture.TestSessionFactory.Key(server, 2); break;
            case "trust-store": backend.Connections.HostKeys.Fail = true; break;
            case "remote-refusal": backend.Start = (_, _, _) => throw new ForwardException(new(TunnelFailureKind.RemoteListenFailure, "Refused by the test.")); break;
        }
        backend.Connections.Factory.Sessions.Single().Disconnect();
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(started.Tunnel.Id).NextReconnectAt is not null);
        await fixture.NextRoundAsync();
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(started.Tunnel.Id).State == TunnelState.Failed && fixture.Reconnect.ActiveServerCount == 0);
        Assert.Equal(expected, fixture.Tunnel(started.Tunnel.Id).LastError!.Kind);
        int requests = backend.Connections.Factory.Requests.Count;
        fixture.Clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(requests, backend.Connections.Factory.Requests.Count);
        Assert.Equal(approvals, backend.Connections.Approval.Requests.Count);
        Assert.All(backend.Attempts, rule => Assert.Equal(started.Tunnel.Requested.RemotePort, rule.RemotePort));
    }

    [Fact]
    public async Task RecoveryWindowIncludesBackoffAndEndsWithOneFailure()
    {
        await using var fixture = await ReconnectFixture.CreateAsync(TimeSpan.FromSeconds(5));
        var backend = fixture.Backend;
        var started = await backend.Manager.StartAsync(backend.Launch());
        int attempts = 0;
        backend.Connections.Factory.BeforeConnect = (_, _) => { Interlocked.Increment(ref attempts); throw new ConnectionException(ConnectionFailure.ConnectionFailed); };
        backend.Connections.Factory.Sessions.Single().Disconnect();
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(started.Tunnel.Id).NextReconnectAt is not null);
        for (int round = 1; round <= 2; round++)
        {
            await fixture.NextRoundAsync();
            int expected = round;
            await ReconnectFixture.UntilAsync(() => attempts == expected && fixture.Tunnel(started.Tunnel.Id).NextReconnectAt is not null);
        }
        await fixture.NextRoundAsync();
        await ReconnectFixture.UntilAsync(() => fixture.Tunnel(started.Tunnel.Id).State == TunnelState.Failed && fixture.Reconnect.ActiveServerCount == 0);
        Assert.Equal(2, attempts); Assert.Contains("time limit", fixture.Tunnel(started.Tunnel.Id).LastError!.Message);
        await fixture.Activity.FlushAsync();
        Assert.Single(backend.Connections.Configuration.Current!.RecentActivity.Where(entry => entry.Outcome == ActivityOutcome.Failed));
        Assert.Equal(1, backend.Connections.Configuration.Current.Statistics.TotalForwards);
    }
}

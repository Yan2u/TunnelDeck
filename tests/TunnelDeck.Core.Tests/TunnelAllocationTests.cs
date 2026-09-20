using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Tests;

public sealed class TunnelAllocationTests
{
    [Fact]
    public async Task OnlyTheLocalListenerAdvancesAndTheFirstAvailablePortWins()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        fixture.Start = (session, rule, _) => rule.LocalPort < 3003
            ? Task.FromException<IForwardHandle>(Occupied()) : Task.FromResult<IForwardHandle>(fixture.CreateHandle(session, rule));
        var requested = fixture.Launch();
        TunnelStartResult result = await fixture.Manager.StartAsync(requested, ConnectionFixture.Input);
        Assert.True(result.Succeeded);
        Assert.Equal(requested.Requested, result.Tunnel.Requested);
        Assert.Equal(requested.Source, result.Tunnel.Source);
        Assert.Equal(3003, result.Tunnel.Actual!.Local.Port);
        Assert.Equal(requested.Requested.GetRoute().Remote, result.Tunnel.Actual.Remote);
        Assert.Equal(PortAdjustmentReason.LocalListenerInUse, result.Tunnel.AdjustmentReason);
        Assert.Equal(4, result.Tunnel.BindAttempts);
        Assert.Equal(Enumerable.Range(3000, 4), fixture.Attempts.Select(rule => rule.LocalPort));
        Assert.Single(fixture.Handles);
    }

    [Theory]
    [InlineData(ForwardMode.Local, false)]
    [InlineData(ForwardMode.Remote, false)]
    [InlineData(ForwardMode.Remote, true)]
    public async Task DisabledLocalSearchAndAllRemoteRequestsTryExactlyOnce(ForwardMode mode, bool auto)
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        fixture.Start = (_, _, _) => Task.FromException<IForwardHandle>(Occupied());
        var launch = fixture.Launch(mode: mode, auto: auto);
        launch = launch with { Requested = launch.Requested with { AutoFindAvailablePort = auto } };
        TunnelStartResult result = await fixture.Manager.StartAsync(launch, ConnectionFixture.Input);
        Assert.False(result.Succeeded);
        Assert.Equal(TunnelState.Failed, result.Tunnel.State);
        Assert.Single(fixture.Attempts);
        if (mode == ForwardMode.Remote) { Assert.False(fixture.Attempts.Single().AutoFindAvailablePort); }
        Assert.Equal(PortSearchStopReason.RetryDisabled, result.Tunnel.PortSearchStopReason);
        Assert.Null(result.Tunnel.Actual);
        Assert.Null(result.Tunnel.AdjustmentReason);
        Assert.Equal(0, fixture.Connections.Manager.TrackedConnectionCount);
    }

    [Theory]
    [InlineData(TunnelFailureKind.LocalAccessDenied)]
    [InlineData(TunnelFailureKind.RemoteListenFailure)]
    [InlineData(TunnelFailureKind.Connectivity)]
    [InlineData(TunnelFailureKind.Credentials)]
    [InlineData(TunnelFailureKind.TimeoutUnknownOutcome)]
    public async Task OtherFailuresNeverSearchAnotherLocalPort(TunnelFailureKind kind)
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        fixture.Start = (_, _, _) => Task.FromException<IForwardHandle>(new ForwardException(new(kind, "Synthetic safe forwarding error.")));
        TunnelStartResult result = await fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        Assert.Equal(kind, result.Tunnel.LastError!.Kind);
        Assert.Equal(TunnelState.Failed, result.Tunnel.State);
        Assert.Single(fixture.Attempts);
        Assert.False(fixture.Connections.Manager.IsServerInUse(result.Tunnel.ServerId));
    }

    [Theory]
    [InlineData(3000, 20, PortSearchStopReason.AttemptLimitReached)]
    [InlineData(65535, 1, PortSearchStopReason.PortLimitReached)]
    [InlineData(65532, 4, PortSearchStopReason.PortLimitReached)]
    public async Task ExhaustionHasFiniteAttemptsAndReleasesTheConnection(int port, int count, PortSearchStopReason reason)
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        fixture.Start = (_, _, _) => Task.FromException<IForwardHandle>(Occupied());
        TunnelStartResult result = await fixture.Manager.StartAsync(fixture.Launch(port: port), ConnectionFixture.Input);
        Assert.Equal(count, result.Tunnel.BindAttempts);
        Assert.Equal(reason, result.Tunnel.PortSearchStopReason);
        Assert.Equal(TunnelFailureKind.LocalAddressInUse, result.Tunnel.LastError!.Kind);
        Assert.Equal(Enumerable.Range(port, count), fixture.Attempts.Select(rule => rule.LocalPort));
        Assert.Equal(0, fixture.Connections.Manager.TrackedConnectionCount);
    }

    [Fact]
    public async Task APerAttemptDeadlineOwnsTheLateHandleAndNeverAdvancesToAnotherPort()
    {
        var clock = new TimerTimeProvider();
        await using var fixture = await TunnelFixture.CreateAsync(clock);
        using var gate = new AsyncGate();
        fixture.Start = async (session, rule, _) => { var handle = fixture.CreateHandle(session, rule); await gate.WaitAsync(); return handle; };
        Task<TunnelStartResult> start = fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(2));
        await fixture.Connections.Factory.Sessions.Single().Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(start.IsCompleted);
        Assert.True(fixture.Connections.Manager.IsServerInUse(DomainData.Id(1)));
        gate.Release.TrySetResult();
        TunnelStartResult result = await start;
        Assert.Equal(TunnelState.Failed, result.Tunnel.State);
        Assert.Equal(TunnelFailureKind.TimeoutUnknownOutcome, result.Tunnel.LastError!.Kind);
        Assert.Single(fixture.Attempts);
        Assert.True(fixture.Handles.Single().Disposed);
        Assert.False(fixture.Connections.Manager.IsServerInUse(DomainData.Id(1)));
    }

    [Fact]
    public async Task ExpiredTotalBudgetDoesNotIssueAnotherAttemptEvenIfThePreviousFailureWasOccupation()
    {
        var clock = new TimerTimeProvider();
        await using var fixture = await TunnelFixture.CreateAsync(clock, ports: new PortSearchPolicy(clock, TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(2)));
        fixture.Start = (_, _, _) => { clock.Advance(TimeSpan.FromMilliseconds(750)); return Task.FromException<IForwardHandle>(Occupied()); };
        TunnelStartResult result = await fixture.Manager.StartAsync(fixture.Launch(), ConnectionFixture.Input);
        Assert.Equal(TunnelFailureKind.TimeoutUnknownOutcome, result.Tunnel.LastError!.Kind);
        Assert.Equal(4, fixture.Attempts.Count);
        Assert.Equal(PortSearchStopReason.DeadlineExceeded, result.Tunnel.PortSearchStopReason);
    }

    [Fact]
    public async Task MissingCredentialsFailBeforeBindingAnyPort()
    {
        await using var fixture = await TunnelFixture.CreateAsync();
        TunnelStartResult result = await fixture.Manager.StartAsync(fixture.Launch());
        Assert.Equal(TunnelState.Failed, result.Tunnel.State);
        Assert.Equal(TunnelFailureKind.Credentials, result.Tunnel.LastError!.Kind);
        Assert.Empty(fixture.Attempts);
        Assert.Equal(0, fixture.Connections.Manager.TrackedConnectionCount);
    }

    private static ForwardException Occupied() => new(new(TunnelFailureKind.LocalAddressInUse, "Synthetic local occupation."));
}

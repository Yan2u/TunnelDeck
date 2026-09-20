using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Profiles;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class ReconnectCommandTests
{
    [Theory]
    [InlineData(ForwardMode.Local)]
    [InlineData(ForwardMode.Remote)]
    public async Task SubmittingTheSameQuickForwardDuringRecoveryDoesNotAskForAnotherTemporaryPassword(ForwardMode mode)
    {
        await using var fixture = await WorkspaceFixture.CreateAsync(remember: false);
        await using var reconnect = new ReconnectCoordinator(fixture.Backend.Manager, clock: new TimerTimeProvider());
        fixture.Workspace.Quick.ModeIndex = (int)mode;
        await fixture.RunAsync(fixture.Workspace.SubmitQuickAsync());
        Guid id = Assert.Single(fixture.Workspace.Items).Id;
        Assert.Equal(1, fixture.PromptCount);
        fixture.Backend.Connections.Factory.Sessions.Single().Disconnect();
        await fixture.Dispatcher.UntilAsync(() => fixture.Backend.Manager.Current.Tunnels.Single().NextReconnectAt is not null);

        await fixture.RunAsync(fixture.Workspace.SubmitQuickAsync());

        Assert.Equal(1, fixture.PromptCount);
        var row = Assert.Single(fixture.Workspace.Items);
        Assert.Equal(id, row.Id); Assert.Equal(TunnelState.Reconnecting, row.Value.State);
        Assert.True(row.CanStop); Assert.False(row.CanRetry); Assert.Empty(fixture.Workspace.CommandError);
    }

    [Fact]
    public async Task ADifferentLocalForwardCannotBorrowCredentialsFromABrokenTransport()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync(remember: false);
        await using var reconnect = new ReconnectCoordinator(fixture.Backend.Manager, clock: new TimerTimeProvider());
        await fixture.RunAsync(fixture.Workspace.SubmitQuickAsync());
        fixture.Backend.Connections.Factory.Sessions.Single().Disconnect();
        await fixture.Dispatcher.UntilAsync(() => fixture.Backend.Manager.Current.Tunnels.Single().NextReconnectAt is not null);
        fixture.Workspace.Quick.PortText = "3001";
        await fixture.RunAsync(fixture.Workspace.SubmitQuickAsync());
        Assert.Equal(2, fixture.PromptCount);
        Assert.Equal(2, fixture.Workspace.Items.Count);
        Assert.Single(fixture.Workspace.Items, row => row.Value.IsRunning);
    }

    [Theory]
    [InlineData(ForwardMode.Local)]
    [InlineData(ForwardMode.Remote)]
    public async Task ProfileStartReportsAnExistingRecoveryWithoutStartingOrFailingItAgain(ForwardMode mode)
    {
        await using var fixture = await ProfileFixture.CreateAsync(rules: [DomainData.Rule(mode: mode)]);
        await using var reconnect = new ReconnectCoordinator(fixture.Backend.Manager, clock: new TimerTimeProvider());
        var first = await fixture.Runner.StartAllAsync(fixture.Profile.Id);
        fixture.Backend.Connections.Factory.Sessions.Single().Disconnect();
        await ReconnectFixture.UntilAsync(() => fixture.Backend.Manager.Current.Tunnels.Single().NextReconnectAt is not null);
        var repeated = await fixture.Runner.StartAllAsync(fixture.Profile.Id);
        Assert.Single(fixture.Prompts);
        Assert.Equal(1, repeated.AlreadyReconnectingCount);
        Assert.Equal(0, repeated.StartedCount); Assert.Equal(0, repeated.FailedCount);
        Assert.Equal(first.Rules.Single().TunnelId, repeated.Rules.Single().TunnelId);
        Assert.Null(repeated.Rules.Single().Failure);
        Assert.Equal("1 already reconnecting", repeated.Summary);
    }

    [Fact]
    public void RecoveryDisplayDistinguishesThePreviousListenerAndUpdatesTheWaitWithoutChangingIdentity()
    {
        var value = new ActiveTunnel
        {
            Id = Guid.NewGuid(), State = TunnelState.Reconnecting, Requested = DomainData.Rule(),
            Actual = DomainData.Rule().GetRoute(), StartedAt = DomainData.Now.AddMinutes(-1),
            ReconnectAttempt = 2, NextReconnectAt = DomainData.Now.AddSeconds(5),
            LastError = new(TunnelFailureKind.Connectivity, "The SSH connection was lost."),
        };
        var row = new TunnelItem(value, DomainData.State([DomainData.Server()], []), DomainData.Now);
        Assert.Equal("Previous listener", row.ActualLabel); Assert.Empty(row.Error);
        Assert.Contains("5 s", row.ReconnectMessage); Assert.Contains("attempt 2", row.ReconnectMessage);
        row.RefreshTime(DomainData.Now.AddSeconds(2));
        Assert.Contains("3 s", row.ReconnectMessage); Assert.Equal(value.Id, row.Id);
        row.Update(value with { State = TunnelState.Running, ReconnectCount = 1, LastConnectionAt = DomainData.Now.AddSeconds(5), LastError = null }, new());
        Assert.Equal("Actual", row.ActualLabel); Assert.Empty(row.ReconnectMessage);
        Assert.Contains("Automatic recoveries: 1", row.ReconnectSummary);
    }
}

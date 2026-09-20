using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Tests;

public sealed class TunnelWorkspaceTests
{
    [Fact]
    public async Task RepeatedSubmitUsesOneSnapshotAndDoesNotCountADuplicateStart()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        using var gate = new AsyncGate();
        fixture.Backend.Start = async (session, rule, token) => { await gate.WaitAsync(token); return fixture.Backend.CreateHandle(session, rule); };
        var workspace = fixture.Workspace;
        Task start = workspace.SubmitQuickAsync();
        await fixture.Dispatcher.UntilAsync(() => gate.Entered.Task.IsCompleted);
        Assert.True(workspace.Quick.IsBusy);
        Assert.False(workspace.Quick.CanStart);
        workspace.Quick.PortText = "4000";
        await fixture.RunAsync(workspace.SubmitQuickAsync());
        gate.Release.TrySetResult();
        await fixture.RunAsync(start);
        Assert.Equal(3000, Assert.Single(fixture.Backend.Attempts).LocalPort);
        Assert.Equal("4000", workspace.Quick.PortText);
        TunnelItem row = Assert.Single(workspace.Items);
        workspace.Quick.PortText = "3000";
        await fixture.RunAsync(workspace.SubmitQuickAsync());
        Assert.Same(row, Assert.Single(workspace.Items));
        Assert.True(await fixture.Activity.FlushAsync());
        Assert.Equal(1, fixture.Backend.Connections.Configuration.Current!.Statistics.TotalForwards);
    }

    [Fact]
    public async Task StopMovesTheConfirmedResultIntoRecentAndKeepsTheRequestedPort()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        fixture.Backend.Start = (session, rule, _) => rule.LocalPort == 3000
            ? Task.FromException<Core.Abstractions.IForwardHandle>(new ForwardException(new(TunnelFailureKind.LocalAddressInUse, "Occupied.")))
            : Task.FromResult<Core.Abstractions.IForwardHandle>(fixture.Backend.CreateHandle(session, rule));
        await fixture.RunAsync(fixture.Workspace.SubmitQuickAsync());
        TunnelItem row = Assert.Single(fixture.Workspace.Items);
        Assert.True(row.PortAdjusted);
        Assert.Equal("1 active", fixture.Workspace.RuntimeStatus);
        await fixture.RunAsync(fixture.Workspace.StopAsync(row));
        Assert.Empty(fixture.Workspace.Items);
        var state = fixture.Backend.Connections.Configuration.Current!;
        ActivityEntry stopped = state.RecentActivity.First();
        Assert.Equal(ActivityOutcome.Stopped, stopped.Outcome);
        Assert.Equal(3000, stopped.Requested.LocalPort);
        Assert.Equal(3001, stopped.Actual!.Local.Port);
        Assert.Equal(1, state.Statistics.TotalForwards);
        Assert.Equal("3000", fixture.Workspace.Quick.PortText);
    }

    [Fact]
    public async Task RetryAndRemoveKeepFailuresVisibleUntilAnExplicitAction()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        fixture.Workspace.Quick.ModeIndex = 1;
        fixture.Backend.Start = (_, _, _) => throw new ForwardException(new(TunnelFailureKind.RemoteListenFailure, "Remote request refused."));
        await fixture.RunAsync(fixture.Workspace.SubmitQuickAsync());
        TunnelItem failed = Assert.Single(fixture.Workspace.Items);
        Assert.Equal("Failed", failed.Status);
        Assert.True(failed.CanRetry);
        Assert.False(failed.PortAdjusted);
        fixture.Backend.Start = null;
        await fixture.RunAsync(fixture.Workspace.RetryAsync(failed));
        TunnelItem running = Assert.Single(fixture.Workspace.Items);
        Assert.Equal("Running", running.Status);
        Assert.NotEqual(failed.Id, running.Id);
        Assert.False(running.CanRemove);
        await fixture.RunAsync(fixture.Workspace.RemoveAsync(running));
        Assert.Single(fixture.Workspace.Items);
        await fixture.RunAsync(fixture.Workspace.StopAllAsync());
        Assert.Empty(fixture.Workspace.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CredentialInputIsEphemeralAndMissingSavedCredentialsRemainRetryable(bool missingSavedCredential)
    {
        await using var fixture = await WorkspaceFixture.CreateAsync(remember: missingSavedCredential);
        fixture.Backend.Connections.Credentials.Values.Clear();
        fixture.Prompt = _ => Task.FromResult<Core.Abstractions.ConnectionCredentials?>(null);
        await fixture.RunAsync(fixture.Workspace.SubmitQuickAsync());
        if (missingSavedCredential) { Assert.Equal(TunnelFailureKind.Credentials, Assert.Single(fixture.Workspace.Items).Value.LastError!.Kind); }
        else { Assert.Empty(fixture.Workspace.Items); Assert.Empty(fixture.Backend.Connections.Factory.Requests); }
        fixture.Prompt = _ => Task.FromResult<Core.Abstractions.ConnectionCredentials?>(ConnectionFixture.Input);
        await fixture.RunAsync(missingSavedCredential ? fixture.Workspace.RetryAsync(fixture.Workspace.Items.Single()) : fixture.Workspace.SubmitQuickAsync());
        Assert.Equal("Running", Assert.Single(fixture.Workspace.Items).Status);
        Assert.Equal(2, fixture.PromptCount);
        Assert.Equal(missingSavedCredential, fixture.Backend.Connections.Configuration.Current!.Servers[0].CredentialReference is not null);
        Assert.Empty(fixture.Backend.Connections.Credentials.Values);
    }

    [Fact]
    public async Task CleanupFailureKeepsTheSameRowAndCanBeStoppedAgain()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        await fixture.RunAsync(fixture.Workspace.SubmitQuickAsync());
        TunnelItem row = Assert.Single(fixture.Workspace.Items);
        var handle = fixture.Backend.Handles.Single();
        handle.OnDispose = () => handle.DisposeCalls == 1 ? Task.FromException(new IOException("Synthetic cleanup failure.")) : Task.CompletedTask;
        await fixture.RunAsync(fixture.Workspace.StopAsync(row));
        Assert.Same(row, Assert.Single(fixture.Workspace.Items));
        Assert.Equal("Stopping", row.Status);
        Assert.True(row.CanStop);
        await fixture.RunAsync(fixture.Workspace.StopAsync(row));
        Assert.Empty(fixture.Workspace.Items);
    }

    [Fact]
    public async Task FailedActivityCommitCanBeRetriedWithoutLosingTheStopOrDoubleCounting()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        fixture.Backend.Connections.Store.BeforeCommit = () => Task.FromException(new IOException("Synthetic save failure."));
        await fixture.RunAsync(fixture.Workspace.SubmitQuickAsync());
        await fixture.Dispatcher.UntilAsync(() => fixture.Workspace.ActivityError.Length > 0);
        TunnelItem row = Assert.Single(fixture.Workspace.Items);
        await fixture.RunAsync(fixture.Workspace.StopAsync(row));
        Assert.Equal("Stopped", Assert.Single(fixture.Workspace.Items).Status);
        Assert.Equal(0, fixture.Backend.Connections.Manager.TrackedConnectionCount);
        fixture.Backend.Connections.Store.BeforeCommit = null;
        await fixture.RunAsync(fixture.Workspace.RetryActivityAsync());
        Assert.Empty(fixture.Workspace.Items);
        Assert.Empty(fixture.Workspace.ActivityError);
        var state = fixture.Backend.Connections.Configuration.Current!;
        Assert.Equal(1, state.Statistics.TotalForwards);
        Assert.Equal(new[] { ActivityOutcome.Stopped, ActivityOutcome.Started }, state.RecentActivity.Select(entry => entry.Outcome));
    }

    [Fact]
    public async Task BackgroundNotificationsAreDispatchedAndOlderSnapshotsCannotRegressARow()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync();
        var result = await fixture.Backend.Manager.StartAsync(fixture.Backend.Launch(), ConnectionFixture.Input);
        Assert.Empty(fixture.Workspace.Items);
        fixture.Dispatcher.Drain(reverse: true);
        var row = Assert.Single(fixture.Workspace.Items);
        Assert.Equal("Running", row.Status);
        Assert.Equal(result.Tunnel.Id, row.Id);
        await fixture.Backend.Manager.StopAsync(row.Id);
        fixture.Workspace.Dispose();
        fixture.Dispatcher.Drain();
        Assert.Equal("Running", row.Status); // Detached UI objects do not process queued callbacks.
    }

    [Fact]
    public async Task ShutdownCancelsAPendingCredentialDialogBeforeAnyLateRetry()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync(remember: false);
        using var prompt = new AsyncGate();
        fixture.Prompt = async token => { await prompt.WaitAsync(token); return ConnectionFixture.Input; };
        Task start = fixture.Workspace.SubmitQuickAsync();
        await fixture.Dispatcher.UntilAsync(() => prompt.Entered.Task.IsCompleted);
        await fixture.RunAsync(fixture.Workspace.ShutdownAsync(CancellationToken.None));
        await fixture.RunAsync(start);
        Assert.False(fixture.Workspace.Quick.CanStart);
        Assert.Empty(fixture.Backend.Connections.Factory.Sessions);
    }

    [Fact]
    public async Task PreviewCannotSubmitEvenWhenACommandIsInvokedDirectly()
    {
        await using var fixture = await WorkspaceFixture.CreateAsync(preview: true);
        fixture.Workspace.SetPreview([new() { Id = Guid.NewGuid(), Requested = DomainData.Rule(), State = TunnelState.Running }]);
        await fixture.RunAsync(fixture.Workspace.SubmitQuickAsync());
        await fixture.RunAsync(fixture.Workspace.StopAllAsync());
        Assert.False(Assert.Single(fixture.Workspace.Items).CanStop);
        Assert.Empty(fixture.Backend.Connections.Factory.Requests);
    }
}

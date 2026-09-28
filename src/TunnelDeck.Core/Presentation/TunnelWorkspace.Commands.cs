using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Snapshots;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Presentation;

public sealed partial class TunnelWorkspace
{
    public Task SubmitQuickAsync()
    {
        if (_closing || IsPreview || !_available || Quick.IsBusy) { return Task.CompletedTask; }
        RuleLaunchSnapshot? launch = Quick.Capture();
        if (launch is null) { return Task.CompletedTask; }
        ServerWorkLease work;
        try { work = _tunnels.Work.Begin(launch.Server, ServerWorkKind.Forward, [launch], _stopping.Token); }
        catch (Exception) { CommandError = UiText.Get("TunnelWorkspaceCommands_ThisServerIsBeingDeletedOrIsNo"); return Task.CompletedTask; }
        Quick.IsBusy = true;
        return Track(async () =>
        {
            try { await StartWithCredentialRecoveryAsync(launch, null, null, work).ConfigureAwait(false); }
            finally { work.Dispose(); await OnUiAsync(() => Quick.IsBusy = false).ConfigureAwait(false); }
        }, work);
    }

    public Task RetryAsync(TunnelItem item)
    {
        if (!item.CanRetry || _closing) { return Task.CompletedTask; }
        RemoteServer? server = _state.Servers.FirstOrDefault(server => server.Id == item.Value.ServerId);
        if (server is null) { CommandError = UiText.Get("TunnelWorkspaceCommands_ThisServerIsNoLongerAvailableRemoveThe"); return Task.CompletedTask; }
        var launch = new RuleLaunchSnapshot(server, item.Value.Requested, item.Value.Source);
        ServerWorkLease work;
        try { work = _tunnels.Work.Begin(launch.Server, ServerWorkKind.Forward, [launch], _stopping.Token); }
        catch (Exception) { CommandError = UiText.Get("TunnelWorkspaceCommands_ThisServerIsBeingDeletedOrIsNo"); return Task.CompletedTask; }
        item.IsBusy = true;
        return Track(async () =>
        {
            try { await StartWithCredentialRecoveryAsync(launch, item.Id, item.Value.LastError, work).ConfigureAwait(false); }
            finally { work.Dispose(); await OnUiAsync(() => { item.IsBusy = false; OnPropertyChanged(nameof(CanStopAll)); }).ConfigureAwait(false); }
        }, work);
    }

    private async Task StartWithCredentialRecoveryAsync(RuleLaunchSnapshot launch, Guid? previousId, TunnelFailure? failure, ServerWorkLease work)
    {
        CancellationToken token = work.Token;
        // Missing optional credentials are an input step, not a failed forwarding attempt.
        // A duplicate or an existing Local transport can be reused without asking for a secret again.
        bool reusable = _tunnels.CanReuseForStart(launch);
        bool prompted = failure?.Kind == TunnelFailureKind.Credentials ||
            launch.Server.AuthMethod == AuthenticationMethod.Password && launch.Server.CredentialReference is null && !reusable;
        ConnectionCredentials? input = null;
        if (prompted)
        {
            input = await _credentials(launch.Server, failure?.Message ?? UiText.Get("TunnelWorkspaceCommands_EnterTheSSHPasswordToStartThisForward"), token).ConfigureAwait(false);
            if (input is null) { return; }
        }
        token.ThrowIfCancellationRequested();
        if (previousId is Guid id && !await _tunnels.RemoveAsync(id, token).ConfigureAwait(false)) { return; }
        TunnelStartResult result = await _tunnels.StartAsync(launch, input, token, work).ConfigureAwait(false);
        input = null;
        if (!prompted && !result.Reused && result.Tunnel.LastError is { Kind: TunnelFailureKind.Credentials } error)
        {
            input = await _credentials(launch.Server, error.Message, token).ConfigureAwait(false);
            if (input is null) { return; }
            token.ThrowIfCancellationRequested();
            if (!await _tunnels.RemoveAsync(result.Tunnel.Id, token).ConfigureAwait(false)) { return; }
            await _tunnels.StartAsync(launch, input, token, work).ConfigureAwait(false);
        }
    }

    public Task StopAsync(TunnelItem item)
    {
        if (!item.CanStop || _closing) { return Task.CompletedTask; }
        item.IsBusy = true;
        return Track(async () =>
        {
            try
            {
                TunnelStopResult result = await _tunnels.StopAsync(item.Id, _stopping.Token).ConfigureAwait(false);
                if (result.Succeeded && await _activity.FlushAsync(_stopping.Token).ConfigureAwait(false))
                { await _tunnels.RemoveAsync(item.Id, _stopping.Token).ConfigureAwait(false); }
            }
            finally { await OnUiAsync(() => { item.IsBusy = false; OnPropertyChanged(nameof(CanStopAll)); }).ConfigureAwait(false); }
        });
    }

    public Task StopAllAsync() => !CanStopAll ? Task.CompletedTask : Track(async () =>
    {
        var results = await _tunnels.StopAllAsync(_stopping.Token).ConfigureAwait(false);
        if (await _activity.FlushAsync(_stopping.Token).ConfigureAwait(false))
        {
            foreach (var result in results.Where(result => result.Succeeded)) { await _tunnels.RemoveAsync(result.TunnelId, _stopping.Token).ConfigureAwait(false); }
        }
    });

    public Task RemoveAsync(TunnelItem item) => !item.CanRemove || _closing ? Task.CompletedTask : Track(async () =>
    {
        // Preserve a failed activity commit for Retry, even if its stopped row is removed from the workspace.
        await _tunnels.RemoveAsync(item.Id, _stopping.Token).ConfigureAwait(false);
    });

    public Task RetryActivityAsync() => Track(async () =>
    {
        if (!await _activity.FlushAsync(_stopping.Token).ConfigureAwait(false)) { return; }
        foreach (ActiveTunnel stopped in _tunnels.Current.Tunnels.Where(tunnel => tunnel.State == TunnelState.Stopped))
        { await _tunnels.RemoveAsync(stopped.Id, _stopping.Token).ConfigureAwait(false); }
    });

    public void ClearCommandError() => CommandError = string.Empty;
    private Task Track(Func<Task> operation, ServerWorkLease? registered = null)
    {
        lock (_commandSync)
        {
            if (_closing || IsPreview) { registered?.Dispose(); return Task.CompletedTask; }
            Task work = Task.Run(async () =>
            {
                try { await OnUiAsync(() => CommandError = string.Empty).ConfigureAwait(false); await operation().ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                catch (Exception) { await OnUiAsync(() => CommandError = UiText.Get("TunnelWorkspaceCommands_TheOperationCouldNotFinishCheckTheTunnel")).ConfigureAwait(false); }
            });
            _commands.Add(work);
            _ = work.ContinueWith(completed => { lock (_commandSync) { _commands.Remove(completed); } }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return work;
        }
    }
}

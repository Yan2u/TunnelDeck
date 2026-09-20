using System.Collections.Immutable;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;

namespace TunnelDeck.Core.Tunnels;

public sealed partial class TunnelManager
{
    public async Task<TunnelStopResult> StopAsync(Guid tunnelId, CancellationToken cancellationToken = default)
    {
        Task<TunnelStopResult> cleanup;
        TunnelManagerSnapshot? changed = null;
        lock (_sync)
        {
            if (!_entries.TryGetValue(tunnelId, out Entry? entry)) { return new(tunnelId, true, null); }
            if (entry.CleanupComplete) { return new(tunnelId, true, entry.Value); }
            RequestStopUnderLock(entry, TunnelState.Stopped);
            cleanup = EnsureCleanupUnderLock(entry);
            changed = ChangedUnderLock();
        }
        Notify(changed);
        // Cancelling a Stop caller only cancels its wait. The engine still completes the accepted cleanup.
        return await AwaitFinalCleanupAsync(tunnelId, cleanup).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private void RequestStopUnderLock(Entry entry, TunnelState terminalState)
    {
        entry.TerminalState = terminalState;
        entry.RecoverRequested = false;
        if (terminalState == TunnelState.Cancelled) { entry.TerminalFailure = new(TunnelFailureKind.Cancelled, "The forwarding request was cancelled."); }
        entry.Value = entry.Value with { StopRequested = true, State = TunnelState.Stopping, NextReconnectAt = null };
    }

    private Task<TunnelStopResult> EnsureCleanupUnderLock(Entry entry)
    {
        if (entry.CleanupComplete) { return Task.FromResult(new TunnelStopResult(entry.Value.Id, true, entry.Value)); }
        if (entry.Cleanup is null || entry.Cleanup.IsFaulted ||
            (entry.Cleanup.IsCompletedSuccessfully && (!entry.Cleanup.Result.Succeeded || entry.AttemptCleaned && !entry.RecoverRequested)))
        {
            entry.Cleanup = Task.Run(() => CleanupEntryAsync(entry));
        }
        return entry.Cleanup;
    }

    private async Task<TunnelStopResult> AwaitFinalCleanupAsync(Guid tunnelId, Task<TunnelStopResult> cleanup)
    {
        while (true)
        {
            TunnelStopResult result = await cleanup.ConfigureAwait(false);
            if (!result.Succeeded) { return result; }
            lock (_sync)
            {
                if (!_entries.TryGetValue(tunnelId, out Entry? entry) || entry.CleanupComplete) { return result; }
                cleanup = EnsureCleanupUnderLock(entry);
            }
        }
    }

    private async Task<TunnelStopResult> CleanupEntryAsync(Entry entry)
    {
        bool gateHeld = false;
        bool slotHeld = false;
        try
        {
            if (!entry.AttemptCleaned)
            {
                await entry.Stop.CancelAsync().ConfigureAwait(false);
                await entry.StartWork.ConfigureAwait(false);
                await entry.ServerGate.WaitAsync().ConfigureAwait(false);
                gateHeld = true;
                await _cleanupSlots.WaitAsync().ConfigureAwait(false);
                slotHeld = true;
                await entry.Disconnected.DisposeAsync().ConfigureAwait(false);
                if (entry.Handle is { } handle)
                {
                    if (entry.ActivityHandler is not null) { handle.ActivityChanged -= entry.ActivityHandler; }
                    await StopHandleAsync(entry, handle).ConfigureAwait(false);
                    await DisposeHandleAsync(entry, handle).ConfigureAwait(false);
                    entry.Handle = null;
                }
                if (entry.Connection is not null)
                {
                    await entry.Connection.DisposeAsync().ConfigureAwait(false);
                    entry.Connection = null;
                }
                entry.Stop.Dispose();
                entry.AttemptCleaned = true;
            }
            TunnelManagerSnapshot changed;
            ActiveTunnel terminal;
            lock (_sync)
            {
                bool recovering = entry.RecoverRequested && !entry.Value.StopRequested;
                if (!recovering)
                {
                    entry.Reservation?.Dispose();
                    entry.Reservation = null;
                    entry.CleanupComplete = true;
                    if (_requests.GetValueOrDefault(entry.Key) == entry) { _requests.Remove(entry.Key); }
                }
                entry.Value = terminal = entry.Value with
                {
                    State = recovering ? TunnelState.Reconnecting : entry.TerminalState,
                    LastError = entry.TerminalFailure,
                    EndedAt = recovering ? null : _clock.GetUtcNow().ToUniversalTime(),
                    NextReconnectAt = recovering ? entry.Value.NextReconnectAt : null,
                };
                changed = ChangedUnderLock();
            }
            Notify(changed);
            return new(terminal.Id, true, terminal);
        }
        catch (Exception)
        {
            TunnelManagerSnapshot changed;
            ActiveTunnel pending;
            lock (_sync)
            {
                entry.Value = pending = entry.Value with { State = TunnelState.Stopping, LastError = TunnelErrors.Cleanup };
                changed = ChangedUnderLock();
            }
            Notify(changed);
            return new(pending.Id, false, pending, TunnelErrors.Cleanup);
        }
        finally
        {
            if (slotHeld) { _cleanupSlots.Release(); }
            if (gateHeld) { entry.ServerGate.Release(); }
        }
    }

    private async Task StopHandleAsync(Entry entry, IForwardHandle handle)
    {
        using var deadline = new CancellationTokenSource(_options.StopTimeout, _clock);
        Task work = entry.PendingStop ??= handle.StopAsync(deadline.Token);
        try { await work.WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (Exception)
        {
            // An unconfirmed stop cannot leave this transport usable by another operation.
            if (entry.Connection is not null) { await entry.Connection.InvalidateAsync().ConfigureAwait(false); }
            try { await work.ConfigureAwait(false); }
            catch (Exception) { }
            lock (_sync) { entry.TerminalFailure ??= TunnelErrors.UnknownOutcome; }
        }
        entry.PendingStop = null;
    }

    private async Task DisposeHandleAsync(Entry entry, IForwardHandle handle)
    {
        using var deadline = new CancellationTokenSource(_options.StopTimeout, _clock);
        Task work = entry.PendingDispose ??= handle.DisposeAsync().AsTask();
        try { await work.WaitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            if (entry.Connection is not null) { await entry.Connection.InvalidateAsync().ConfigureAwait(false); }
            await work.ConfigureAwait(false);
        }
        finally { if (work.IsCompleted) { entry.PendingDispose = null; } }
    }

    public Task<ImmutableArray<TunnelStopResult>> StopAllAsync(CancellationToken cancellationToken = default) =>
        StopGroupAsync(null, cancellationToken);

    public Task<ImmutableArray<TunnelStopResult>> StopServerAsync(Guid serverId, CancellationToken cancellationToken = default) =>
        StopGroupAsync(serverId, cancellationToken);

    private Task<ImmutableArray<TunnelStopResult>> StopGroupAsync(Guid? serverId, CancellationToken cancellationToken)
    {
        Task<TunnelStopResult>[] pending;
        TunnelManagerSnapshot changed;
        lock (_sync)
        {
            if (serverId is Guid id) { _serverStopBarriers[id] = _serverStopBarriers.GetValueOrDefault(id) + 1; }
            else { _stopAllBarriers++; }
            Entry[] entries = _entries.Values.Where(entry => !entry.CleanupComplete && (serverId is null || entry.Value.ServerId == serverId)).ToArray();
            foreach (Entry entry in entries) { RequestStopUnderLock(entry, TunnelState.Stopped); }
            pending = entries.Select(entry => AwaitFinalCleanupAsync(entry.Value.Id, EnsureCleanupUnderLock(entry))).ToArray();
            changed = ChangedUnderLock();
        }
        Notify(changed);
        Task<ImmutableArray<TunnelStopResult>> drain = DrainGroupAsync(pending, serverId);
        return drain.WaitAsync(cancellationToken);
    }

    private async Task<ImmutableArray<TunnelStopResult>> DrainGroupAsync(Task<TunnelStopResult>[] pending, Guid? serverId)
    {
        try { return [.. await Task.WhenAll(pending).ConfigureAwait(false)]; }
        finally
        {
            lock (_sync)
            {
                if (serverId is Guid id) { _serverStopBarriers[id]--; }
                else { _stopAllBarriers--; }
            }
        }
    }

    public async Task<bool> RemoveAsync(Guid tunnelId, CancellationToken cancellationToken = default)
    {
        TunnelStopResult stopped = await StopAsync(tunnelId, cancellationToken).ConfigureAwait(false);
        if (!stopped.Succeeded) { return false; }
        TunnelManagerSnapshot? changed = null;
        lock (_sync)
        {
            if (_entries.TryGetValue(tunnelId, out Entry? entry) && entry.CleanupComplete)
            {
                _entries.Remove(tunnelId);
                changed = ChangedUnderLock();
            }
        }
        Notify(changed);
        return true;
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        Task pending;
        lock (_sync)
        {
            _accepting = false;
            if (_shutdown is null || _shutdown.IsFaulted) { _shutdown = Task.Run(ShutdownCoreAsync); }
            pending = _shutdown;
        }
        return pending.WaitAsync(cancellationToken);
    }

    private async Task ShutdownCoreAsync()
    {
        ImmutableArray<TunnelStopResult> results = await StopAllAsync().ConfigureAwait(false);
        if (results.Any(result => !result.Succeeded)) { throw new ForwardException(TunnelErrors.Cleanup); }
    }
}

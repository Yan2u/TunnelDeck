using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Snapshots;

namespace TunnelDeck.Core.Tunnels;

internal sealed record ReconnectCandidate(Guid TunnelId, RuleLaunchSnapshot Launch, long Generation);

public sealed partial class TunnelManager
{
    internal void EnableReconnect(bool enabled)
    {
        lock (_sync)
        {
            if (enabled && _reconnectEnabled) { throw new InvalidOperationException(UiText.Invariant("Diagnostic_TunnelManagerReconnect_AReconnectCoordinatorIsAlreadyAttached")); }
            _reconnectEnabled = enabled;
        }
    }

    internal ImmutableArray<ReconnectCandidate> ReconnectCandidates
    {
        get
        {
            lock (_sync)
            {
                return [.. _entries.Values.Where(entry => entry.RecoverRequested && entry.AttemptCleaned &&
                    !entry.CleanupComplete && !entry.Value.StopRequested && entry.Value.State == TunnelState.Reconnecting)
                    .OrderBy(entry => entry.Order).Select(entry => new ReconnectCandidate(entry.Value.Id, entry.Launch, entry.Value.Generation))];
            }
        }
    }

    internal void ScheduleReconnect(Guid tunnelId, int attempt, DateTimeOffset next)
    {
        TunnelManagerSnapshot? changed = null;
        lock (_sync)
        {
            if (_entries.TryGetValue(tunnelId, out Entry? entry) && entry.RecoverRequested && !entry.Value.StopRequested)
            {
                entry.Value = entry.Value with { ReconnectAttempt = attempt, NextReconnectAt = next };
                changed = ChangedUnderLock();
            }
        }
        Notify(changed);
    }

    internal async Task<ActiveTunnel?> ReconnectAsync(Guid tunnelId, ServerWorkLease work, CancellationToken deadline)
    {
        Entry? entry;
        Task previousReady, previousCleanup;
        lock (_sync)
        {
            if (!_entries.TryGetValue(tunnelId, out entry)) { return null; }
            previousReady = entry.Ready;
            previousCleanup = entry.Cleanup ?? Task.CompletedTask;
        }
        // The old attempt, callbacks and control gate must have completely drained before fields are reused.
        await Task.WhenAll(previousReady, previousCleanup).ConfigureAwait(false);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(work.Token, deadline);
        Work.Validate(work, entry.Launch);
        Task ready;
        TunnelManagerSnapshot changed;
        lock (_sync)
        {
            stopping.Token.ThrowIfCancellationRequested();
            RequireStartAllowed(entry.Value.ServerId);
            if (!_entries.ContainsKey(tunnelId) || !entry.RecoverRequested || entry.CleanupComplete ||
                !entry.AttemptCleaned || entry.Value.StopRequested) { return entry.Value; }
            entry.Stop = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token);
            entry.Token = entry.Stop.Token;
            entry.AttemptCleaned = false;
            entry.Cleanup = null;
            entry.TerminalFailure = null;
            entry.TerminalState = TunnelState.Failed;
            entry.Disconnected = default;
            entry.ActivityHandler = null;
            entry.LastActivityRevision = -1;
            entry.LastDiagnosticNotification = null;
            entry.AcceptedBeforeAttempt = entry.Value.AcceptedConnections;
            entry.FailedBeforeAttempt = entry.Value.RequestFailureCount;
            entry.ReservationTask = Task.FromResult(entry.Reservation!);
            entry.Value = entry.Value with { Generation = entry.Value.Generation + 1, LastError = null, NextReconnectAt = null };
            entry.StartWork = Task.Run(() => StartCoreAsync(entry, null));
            entry.Ready = ready = Task.Run(() => CompleteStartAsync(entry));
            changed = ChangedUnderLock();
        }
        using CancellationTokenRegistration cancelled = work.Token.Register(() => _ = StopAsync(tunnelId));
        Notify(changed);
        try { await ready.WaitAsync(stopping.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            if (work.Token.IsCancellationRequested) { await StopAsync(tunnelId).ConfigureAwait(false); }
            else { await FailReconnectAsync(tunnelId, RecoveryExpired).ConfigureAwait(false); }
            throw;
        }
        lock (_sync) { return entry.Value; }
    }

    internal Task<TunnelStopResult> FailReconnectAsync(Guid tunnelId, TunnelFailure failure)
    {
        Task<TunnelStopResult> cleanup;
        TunnelManagerSnapshot changed;
        lock (_sync)
        {
            if (!_entries.TryGetValue(tunnelId, out Entry? entry)) { return Task.FromResult(new TunnelStopResult(tunnelId, true, null)); }
            if (entry.CleanupComplete || entry.Value.State == TunnelState.Running)
            { return Task.FromResult(new TunnelStopResult(tunnelId, true, entry.Value)); }
            entry.RecoverRequested = false;
            if (!entry.Value.StopRequested) { entry.TerminalState = TunnelState.Failed; entry.TerminalFailure = failure; }
            entry.Value = entry.Value with { State = TunnelState.Stopping, LastError = entry.TerminalFailure, NextReconnectAt = null };
            cleanup = EnsureCleanupUnderLock(entry);
            changed = ChangedUnderLock();
        }
        Notify(changed);
        return AwaitFinalCleanupAsync(tunnelId, cleanup);
    }

    internal static TunnelFailure RecoveryExpired => new(TunnelFailureKind.Connectivity,
        UiText.Invariant("Diagnostic_TunnelManagerReconnect_AutomaticReconnectReachedItsTimeLimitCheckThe"));
}

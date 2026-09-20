using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Snapshots;

namespace TunnelDeck.Core.Tunnels;

public sealed partial class TunnelManager
{
    internal bool CanReuseForStart(RuleLaunchSnapshot launch)
    {
        ForwardRequestKey key = ForwardRequestKey.FromRule(launch.Requested);
        return Current.Tunnels.Any(tunnel => !tunnel.StopRequested &&
            (ForwardRequestKey.FromRule(tunnel.Requested) == key &&
                tunnel.State is TunnelState.Starting or TunnelState.Running or TunnelState.Reconnecting ||
             launch.Requested.Mode == ForwardMode.Local && tunnel.Requested.Mode == ForwardMode.Local &&
                tunnel.ServerId == launch.Server.Id && tunnel.State is TunnelState.Starting or TunnelState.Running));
    }

    private async Task<TunnelFailure?> StartCoreAsync(Entry entry, ConnectionCredentials? credentials)
    {
        bool slot = false;
        try
        {
            entry.Reservation = await entry.ReservationTask.ConfigureAwait(false);
            entry.Token.ThrowIfCancellationRequested();
            if (!ServerIdentity.Matches(entry.Reservation.Server, entry.Launch.Server))
            {
                throw new ForwardException(new(TunnelFailureKind.Input, "The server's connection settings changed after this rule was captured. Reopen the rule and retry."));
            }
            await _startSlots.WaitAsync(entry.Token).ConfigureAwait(false);
            slot = true;
            // SSH authentication and host approval happen outside the per-server control gate.
            entry.Connection = entry.RecoverRequested
                ? await _connections.AcquireForReconnectAsync(entry.Reservation, entry.Value.Requested.Mode, entry.Token).ConfigureAwait(false)
                : await _connections.AcquireAsync(entry.Reservation, entry.Value.Requested.Mode, credentials, entry.Token).ConfigureAwait(false);
            long generation = entry.Value.Generation;
            entry.Disconnected = entry.Connection.Session.Disconnected.Register(() => OnDisconnected(entry, generation));
            SemaphoreSlim gate = entry.ServerGate;
            await gate.WaitAsync(entry.Token).ConfigureAwait(false);
            try { await BindAsync(entry).ConfigureAwait(false); }
            finally { gate.Release(); }
            return null;
        }
        catch (Exception error)
        {
            TunnelFailure failure = TunnelErrors.From(error);
            lock (_sync)
            {
                if (!entry.Value.StopRequested && (failure.Kind != TunnelFailureKind.Cancelled || entry.TerminalFailure is null))
                {
                    entry.TerminalFailure = failure;
                }
            }
            return failure;
        }
        finally { if (slot) { _startSlots.Release(); } }
    }

    private async Task BindAsync(Entry entry)
    {
        ForwardRule bindRequest = entry.Value.Requested;
        if (entry.RecoverRequested && bindRequest.Mode == ForwardMode.Local && entry.Value.Actual is { } previous)
        { bindRequest = bindRequest with { LocalPort = previous.Local.Port }; }
        PortSearch search = _ports.Begin(bindRequest, entry.Token);
        PortSearchAttempt? attempt = search.Next();
        TunnelFailure? last = null;
        while (attempt is not null)
        {
            entry.Token.ThrowIfCancellationRequested();
            lock (_sync) { entry.Value = entry.Value with { BindAttempts = attempt.Number }; }
            try
            {
                await StartCandidateAsync(entry, attempt).ConfigureAwait(false);
                search.Next();
                lock (_sync)
                {
                    entry.Value = entry.Value with
                    {
                        Actual = entry.Handle!.Actual,
                        AdjustmentReason = entry.Value.Requested.Mode == ForwardMode.Local && entry.Handle.Actual.Local.Port != entry.Value.Requested.LocalPort
                            ? PortAdjustmentReason.LocalListenerInUse : null,
                        PortSearchStopReason = PortSearchStopReason.Completed,
                    };
                }
                return;
            }
            catch (ForwardException exception)
            {
                last = exception.Failure;
                if (last.Kind != TunnelFailureKind.LocalAddressInUse || entry.Value.Requested.Mode != ForwardMode.Local || !entry.Value.Requested.AutoFindAvailablePort)
                {
                    lock (_sync)
                    {
                        entry.Value = entry.Value with
                        {
                            PortSearchStopReason = last.Kind == TunnelFailureKind.TimeoutUnknownOutcome ? PortSearchStopReason.DeadlineExceeded
                                : entry.Value.Requested.Mode != ForwardMode.Local || !entry.Value.Requested.AutoFindAvailablePort ? PortSearchStopReason.RetryDisabled
                                : PortSearchStopReason.NonRetryableFailure,
                        };
                    }
                    throw;
                }
                attempt = search.Next(last.Kind);
            }
        }
        lock (_sync) { entry.Value = entry.Value with { PortSearchStopReason = search.StopReason }; }
        if (search.StopReason == PortSearchStopReason.Cancelled) { throw new OperationCanceledException(entry.Token); }
        if (search.StopReason == PortSearchStopReason.DeadlineExceeded) { throw new ForwardException(TunnelErrors.UnknownOutcome); }
        if (search.StopReason is PortSearchStopReason.AttemptLimitReached or PortSearchStopReason.PortLimitReached)
        {
            last = new(TunnelFailureKind.LocalAddressInUse, $"No local listening port was available in {search.AttemptsIssued} attempts. The search stopped at its configured limit.");
        }
        throw new ForwardException(last ?? new(TunnelFailureKind.Unknown, "The listening request could not be completed."));
    }

    private async Task StartCandidateAsync(Entry entry, PortSearchAttempt attempt)
    {
        using var deadline = new CancellationTokenSource(attempt.Timeout, _clock);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(entry.Token, deadline.Token);
        long started = _clock.GetTimestamp();
        Task<IForwardHandle> pending = entry.Connection!.Session.StartAsync(attempt.Candidate, stopping.Token);
        try
        {
            entry.Handle = await pending.WaitAsync(stopping.Token).ConfigureAwait(false);
            if (deadline.IsCancellationRequested || _clock.GetElapsedTime(started) >= attempt.Timeout) { throw new TimeoutException(); }
            entry.Token.ThrowIfCancellationRequested();
            if (!entry.Handle.IsStarted) { throw new ForwardException(TunnelErrors.Disconnected); }
        }
        catch (Exception error) when (error is TimeoutException or OperationCanceledException || deadline.IsCancellationRequested || _clock.GetElapsedTime(started) >= attempt.Timeout)
        {
            bool timedOut = deadline.IsCancellationRequested || error is TimeoutException || _clock.GetElapsedTime(started) >= attempt.Timeout;
            await stopping.CancelAsync().ConfigureAwait(false);
            try { await entry.Connection.InvalidateAsync().ConfigureAwait(false); }
            catch (Exception) { /* Retained by the connection manager; cleanup can retry it. */ }
            // Even an adapter ignoring cancellation may return a late listener. Keep that handle for cleanup.
            ForwardException? confirmedFailure = null;
            try { entry.Handle = await pending.ConfigureAwait(false); }
            catch (ForwardException failure) { confirmedFailure = failure; }
            catch (Exception) { }
            if (timedOut) { throw new ForwardException(TunnelErrors.UnknownOutcome); }
            // A rejected Remote request retires its transport. Its disconnect callback can cancel
            // our wait before the adapter returns the precise refusal; retain that evidence.
            if (confirmedFailure is not null) { throw confirmedFailure; }
            throw new OperationCanceledException(entry.Token);
        }
    }

    private async Task CompleteStartAsync(Entry entry)
    {
        TunnelFailure? failure = await entry.StartWork.ConfigureAwait(false);
        TunnelManagerSnapshot changed;
        bool running;
        IForwardHandle? observed = null;
        lock (_sync)
        {
            if (entry.CleanupComplete) { return; }
            running = failure is null && !entry.Value.StopRequested && !entry.Token.IsCancellationRequested &&
                entry.Value.State is TunnelState.Starting or TunnelState.Reconnecting &&
                entry.Connection?.Session.Disconnected.IsCancellationRequested == false;
            if (running)
            {
                DateTimeOffset now = _clock.GetUtcNow().ToUniversalTime();
                entry.Value = entry.Value with
                {
                    State = TunnelState.Running, StartedAt = entry.Value.StartedAt ?? now, LastConnectionAt = now,
                    LastError = null, NextReconnectAt = null, ReconnectCount = entry.Value.ReconnectCount + (entry.RecoverRequested ? 1 : 0),
                };
                entry.RecoverRequested = false;
                long generation = entry.Value.Generation;
                entry.ActivityHandler = activity => OnActivity(entry, generation, activity);
                entry.Handle!.ActivityChanged += entry.ActivityHandler;
                observed = entry.Handle;
            }
            else
            {
                if (!entry.Value.StopRequested) { entry.TerminalFailure ??= failure ?? TunnelErrors.Disconnected; }
                entry.Value = entry.Value with { State = entry.RecoverRequested && !entry.Value.StopRequested ? TunnelState.Reconnecting : TunnelState.Stopping, LastError = entry.TerminalFailure };
            }
            changed = ChangedUnderLock();
        }
        Notify(changed);
        if (running) { OnActivity(entry, entry.Value.Generation, observed!.Activity); }
        else
        {
            Task<TunnelStopResult> cleanup;
            lock (_sync) { cleanup = EnsureCleanupUnderLock(entry); }
            await cleanup.ConfigureAwait(false);
        }
    }

    private void OnDisconnected(Entry entry, long generation)
    {
        TunnelManagerSnapshot? changed;
        lock (_sync)
        {
            if (entry.CleanupComplete || entry.Value.Generation != generation) { return; }
            if (_reconnectEnabled && _accepting && !entry.Value.StopRequested && entry.Value.State == TunnelState.Running)
            { entry.RecoverRequested = true; }
            if (!entry.Value.StopRequested) { entry.TerminalState = TunnelState.Failed; entry.TerminalFailure ??= TunnelErrors.Disconnected; }
            entry.Value = entry.Value with { State = entry.RecoverRequested && !entry.Value.StopRequested ? TunnelState.Reconnecting : TunnelState.Stopping, LastError = entry.TerminalFailure };
            EnsureCleanupUnderLock(entry);
            changed = ChangedUnderLock();
        }
        Notify(changed);
    }

    private void OnActivity(Entry entry, long generation, ForwardActivity activity)
    {
        TunnelManagerSnapshot? changed = null;
        lock (_sync)
        {
            if (entry.Value.Generation != generation || entry.Value.State != TunnelState.Running || entry.CleanupComplete || activity.Revision <= entry.LastActivityRevision) { return; }
            entry.LastActivityRevision = activity.Revision;
            bool newFailure = entry.FailedBeforeAttempt + activity.FailedConnections > entry.Value.RequestFailureCount;
            entry.Value = entry.Value with
            {
                AcceptedConnections = entry.AcceptedBeforeAttempt + activity.AcceptedConnections,
                RequestFailureCount = entry.FailedBeforeAttempt + activity.FailedConnections,
                LastRequestAt = activity.LastRequestAt ?? entry.Value.LastRequestAt,
                LastRequestError = activity.LastFailure ?? entry.Value.LastRequestError,
                LastRequestFailureAt = activity.LastFailureAt ?? entry.Value.LastRequestFailureAt,
            };
            if (activity.ListenerStopped)
            {
                entry.TerminalFailure = new(TunnelFailureKind.Connectivity, "The listener stopped unexpectedly. Retry this forward after cleanup.");
                entry.RecoverRequested = _reconnectEnabled && _accepting && !entry.Value.StopRequested;
                entry.Value = entry.Value with { State = entry.RecoverRequested ? TunnelState.Reconnecting : TunnelState.Stopping, LastError = entry.TerminalFailure };
                EnsureCleanupUnderLock(entry);
                changed = ChangedUnderLock();
            }
            else if (newFailure && (entry.LastDiagnosticNotification is null ||
                _clock.GetElapsedTime(entry.LastDiagnosticNotification.Value) >= _options.DiagnosticNotificationInterval))
            {
                entry.LastDiagnosticNotification = _clock.GetTimestamp();
                changed = ChangedUnderLock();
            }
            else { _revision++; }
        }
        Notify(changed);
    }
}

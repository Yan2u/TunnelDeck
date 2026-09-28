using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Lifecycle;

/// <summary>One bounded recovery loop per server. The tunnel manager continues to own every native handle.</summary>
public sealed class ReconnectCoordinator : IShutdownParticipant, IAsyncDisposable
{
    private readonly TunnelManager _tunnels;
    private readonly ReconnectPolicy _policy;
    private readonly TimeProvider _clock;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, ServerRecovery> _servers = [];
    private readonly HashSet<Task> _tasks = [];
    private readonly CancellationTokenSource _closing = new();
    private bool _stopping;
    public int ActiveServerCount { get { lock (_sync) { return _servers.Count; } } }

    public ReconnectCoordinator(TunnelManager tunnels, ReconnectOptions? options = null, TimeProvider? clock = null, Func<double>? random = null)
    {
        _tunnels = tunnels; _clock = clock ?? TimeProvider.System; _policy = new(options, random);
        tunnels.EnableReconnect(true);
        tunnels.StateChanged += OnStateChanged;
        tunnels.Work.AvailabilityChanged += Synchronize;
    }

    private void OnStateChanged(TunnelManagerSnapshot snapshot) => Synchronize();

    private void Synchronize()
    {
        ReconnectCandidate[] candidates = [.. _tunnels.ReconnectCandidates];
        var schedules = new List<(Guid Id, int Attempt, DateTimeOffset At)>();
        lock (_sync)
        {
            foreach (ServerRecovery recovery in _servers.Values) { recovery.Pulse(); }
            if (_stopping) { return; }
            foreach (ReconnectCandidate candidate in candidates)
            {
                Guid serverId = candidate.Launch.Server.Id;
                _servers.TryGetValue(serverId, out ServerRecovery? recovery);
                if (recovery is { Finishing: true } || recovery?.Tickets.ContainsKey(candidate.TunnelId) == true) { continue; }
                ServerWorkLease work;
                try { work = _tunnels.Work.Begin(candidate.Launch.Server, ServerWorkKind.Reconnect, [candidate.Launch], _closing.Token); }
                catch (ConnectionException) { continue; } // A temporary deletion barrier is retried when it is released.
                catch (OperationCanceledException) { continue; }
                work.Bind(candidate.Launch.Requested.Id, candidate.TunnelId);
                bool start = recovery is null;
                if (start) { _servers.Add(serverId, recovery = new(serverId, _clock, _policy.RecoveryWindow)); }
                recovery!.Tickets.Add(candidate.TunnelId, new(candidate.TunnelId, work, recovery));
                if (recovery.NextAt is DateTimeOffset next) { schedules.Add((candidate.TunnelId, recovery.Round, next)); }
                if (start)
                {
                    Task task = Task.Run(() => RunServerAsync(recovery));
                    _tasks.Add(task);
                    _ = task.ContinueWith(done =>
                    {
                        _ = done.Exception;
                        lock (_sync) { _tasks.Remove(done); }
                    }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                }
            }
        }
        foreach (var schedule in schedules) { _tunnels.ScheduleReconnect(schedule.Id, schedule.Attempt, schedule.At); }
    }

    private RecoveryTicket[] Tickets(ServerRecovery recovery)
    { lock (_sync) { return [.. recovery.Tickets.Values]; } }

    private async Task RunServerAsync(ServerRecovery recovery)
    {
        TunnelFailure failure = TunnelManager.RecoveryExpired;
        int rounds = 0;
        try
        {
            while (true)
            {
                await RetireFinishedAsync(recovery).ConfigureAwait(false);
                lock (_sync)
                {
                    if (recovery.Tickets.Count == 0) { recovery.Finishing = true; return; }
                }
                TimeSpan remaining = _policy.RecoveryWindow - _clock.GetElapsedTime(recovery.Started);
                if (remaining <= TimeSpan.Zero) { return; }
                TimeSpan delay = _policy.Delay(rounds, remaining);
                DateTimeOffset next = _clock.GetUtcNow() + delay;
                lock (_sync) { recovery.Round = rounds + 1; recovery.NextAt = next; }
                foreach (RecoveryTicket ticket in Tickets(recovery)) { _tunnels.ScheduleReconnect(ticket.TunnelId, rounds + 1, next); }
                if (!await WaitForRoundAsync(recovery, delay).ConfigureAwait(false)) { return; }
                lock (_sync) { recovery.NextAt = null; }
                foreach (RecoveryTicket ticket in Tickets(recovery))
                {
                    recovery.Deadline.Token.ThrowIfCancellationRequested();
                    if (ticket.Work.Token.IsCancellationRequested) { await StopTicketAsync(recovery, ticket).ConfigureAwait(false); continue; }
                    ActiveTunnel? result;
                    try { result = await _tunnels.ReconnectAsync(ticket.TunnelId, ticket.Work, recovery.Deadline.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (ticket.Work.Token.IsCancellationRequested)
                    { await StopTicketAsync(recovery, ticket).ConfigureAwait(false); continue; }
                    if (result is { State: TunnelState.Reconnecting } && !ReconnectPolicy.CanRetry(result.LastError))
                    { await _tunnels.FailReconnectAsync(ticket.TunnelId, result.LastError ?? new(TunnelFailureKind.Unknown, UiText.Invariant("Diagnostic_ReconnectCoordinator_TheForwardCouldNotBeRestoredRetryIt"))).ConfigureAwait(false); }
                }
                rounds++;
            }
        }
        catch (OperationCanceledException) { /* A deadline fails remaining rows; a cancelled work lease stops its row. */ }
        catch (Exception) { failure = new(TunnelFailureKind.Unknown, UiText.Invariant("Diagnostic_ReconnectCoordinator_AutomaticReconnectCouldNotCompleteCheckTheForward")); }
        finally
        {
            lock (_sync) { recovery.Finishing = true; }
            foreach (RecoveryTicket ticket in Tickets(recovery))
            {
                try
                {
                    if (ticket.Work.Token.IsCancellationRequested) { await _tunnels.StopAsync(ticket.TunnelId).ConfigureAwait(false); }
                    else { await _tunnels.FailReconnectAsync(ticket.TunnelId, failure).ConfigureAwait(false); }
                }
                finally { RemoveTicket(recovery, ticket); }
            }
            recovery.Dispose();
            lock (_sync) { _servers.Remove(recovery.ServerId); }
            Synchronize();
        }
    }

    private async Task<bool> WaitForRoundAsync(ServerRecovery recovery, TimeSpan delay)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(recovery.Deadline.Token);
        Task elapsed = Task.Delay(delay, _clock, stopping.Token);
        try
        {
            while (!elapsed.IsCompleted)
            {
                Task changed = recovery.Change;
                await Task.WhenAny(elapsed, changed).ConfigureAwait(false);
                if (changed.IsCompleted)
                {
                    recovery.Acknowledge(changed);
                    await RetireFinishedAsync(recovery).ConfigureAwait(false);
                    lock (_sync) { if (recovery.Tickets.Count == 0) { recovery.Finishing = true; return false; } }
                }
            }
            await elapsed.ConfigureAwait(false);
            recovery.Deadline.Token.ThrowIfCancellationRequested();
            return true;
        }
        finally { await stopping.CancelAsync().ConfigureAwait(false); }
    }

    private async Task RetireFinishedAsync(ServerRecovery recovery)
    {
        var current = _tunnels.Current.Tunnels.ToDictionary(tunnel => tunnel.Id);
        foreach (RecoveryTicket ticket in Tickets(recovery))
        {
            if (ticket.Work.Token.IsCancellationRequested) { await StopTicketAsync(recovery, ticket).ConfigureAwait(false); }
            else if (!current.TryGetValue(ticket.TunnelId, out ActiveTunnel? tunnel) || tunnel.State != TunnelState.Reconnecting)
            {
                if (tunnel is { StopRequested: true }) { await _tunnels.StopAsync(ticket.TunnelId).ConfigureAwait(false); }
                RemoveTicket(recovery, ticket);
            }
        }
    }

    private async Task StopTicketAsync(ServerRecovery recovery, RecoveryTicket ticket)
    {
        await _tunnels.StopAsync(ticket.TunnelId).ConfigureAwait(false);
        RemoveTicket(recovery, ticket);
    }

    private void RemoveTicket(ServerRecovery recovery, RecoveryTicket ticket)
    {
        bool removed;
        lock (_sync) { removed = recovery.Tickets.Remove(ticket.TunnelId); }
        if (removed) { ticket.Dispose(); }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        Task[] pending;
        lock (_sync) { _stopping = true; pending = [.. _tasks]; }
        _tunnels.EnableReconnect(false);
        Task[] stopping = [.. _tunnels.Current.Tunnels.Where(tunnel => tunnel.State == TunnelState.Reconnecting)
            .Select(tunnel => _tunnels.StopAsync(tunnel.Id))];
        await _closing.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(pending.Concat(stopping)).WaitAsync(cancellationToken).ConfigureAwait(false);
        _tunnels.StateChanged -= OnStateChanged;
        _tunnels.Work.AvailabilityChanged -= Synchronize;
    }

    public ValueTask DisposeAsync() => new(ShutdownAsync(CancellationToken.None));

    private sealed class RecoveryTicket : IDisposable
    {
        private readonly CancellationTokenRegistration _cancelled;
        public Guid TunnelId { get; }
        public ServerWorkLease Work { get; }
        public RecoveryTicket(Guid tunnelId, ServerWorkLease work, ServerRecovery recovery)
        { TunnelId = tunnelId; Work = work; _cancelled = work.Token.Register(recovery.Pulse); }
        public void Dispose() { _cancelled.Dispose(); Work.Dispose(); }
    }

    private sealed class ServerRecovery : IDisposable
    {
        private readonly object _signalSync = new();
        private TaskCompletionSource _change = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _budget;
        public Guid ServerId { get; }
        public long Started { get; }
        public CancellationTokenSource Deadline { get; }
        public Dictionary<Guid, RecoveryTicket> Tickets { get; } = [];
        public bool Finishing { get; set; }
        public int Round { get; set; }
        public DateTimeOffset? NextAt { get; set; }
        public ServerRecovery(Guid serverId, TimeProvider clock, TimeSpan budget)
        {
            ServerId = serverId; Started = clock.GetTimestamp();
            _budget = new(budget, clock);
            Deadline = CancellationTokenSource.CreateLinkedTokenSource(_budget.Token);
        }
        public Task Change { get { lock (_signalSync) { return _change.Task; } } }
        public void Pulse() { lock (_signalSync) { _change.TrySetResult(); } }
        public void Acknowledge(Task observed)
        { lock (_signalSync) { if (_change.Task == observed) { _change = new(TaskCreationOptions.RunContinuationsAsynchronously); } } }
        public void Dispose() { Deadline.Dispose(); _budget.Dispose(); }
    }
}

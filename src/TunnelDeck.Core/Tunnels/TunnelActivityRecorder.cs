using System.Collections.Immutable;
using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Runtime;

namespace TunnelDeck.Core.Tunnels;

/// <summary>Persists discrete lifecycle events, never request counters or duration ticks. Failed commits remain retryable.</summary>
public sealed class TunnelActivityRecorder : IShutdownParticipant, IDisposable
{
    private readonly TunnelManager _tunnels;
    private readonly ConfigurationService _configuration;
    private readonly object _sync = new();
    private readonly HashSet<Guid> _started = [], _ended = [], _profileUses = [];
    private readonly Queue<PendingWrite> _pending = new();
    private Task? _drain;
    private bool _draining, _disposed;
    private string _error = string.Empty;
    public string Error { get { lock (_sync) { return _error; } } }
    public event Action? ErrorChanged;

    public TunnelActivityRecorder(TunnelManager tunnels, ConfigurationService configuration)
    {
        _tunnels = tunnels; _configuration = configuration;
        tunnels.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(TunnelManagerSnapshot state)
    {
        lock (_sync)
        {
            if (_disposed) { return; }
            foreach (ActiveTunnel tunnel in state.Tunnels)
            {
                // A newer terminal snapshot can arrive before Running. StartedAt retains that evidence.
                if (tunnel.StartedAt is DateTimeOffset started && _started.Add(tunnel.Id)) { Enqueue(tunnel, ActivityOutcome.Started, started); }
                if (tunnel.EndedAt is DateTimeOffset ended && _ended.Add(tunnel.Id))
                {
                    Enqueue(tunnel, tunnel.State switch { TunnelState.Stopped => ActivityOutcome.Stopped, TunnelState.Cancelled => ActivityOutcome.Cancelled, _ => ActivityOutcome.Failed }, ended);
                }
            }
            EnsureDrainUnderLock();
        }
    }

    private void Enqueue(ActiveTunnel tunnel, ActivityOutcome outcome, DateTimeOffset occurred) => _pending.Enqueue(new(new()
    {
        Id = Guid.NewGuid(), OccurredAt = occurred.ToUniversalTime(), Outcome = outcome,
        Requested = tunnel.Requested with { AutoFindAvailablePort = tunnel.Requested.Mode == ForwardMode.Local && tunnel.Requested.AutoFindAvailablePort },
        Actual = tunnel.Actual, Source = tunnel.Source, Error = outcome == ActivityOutcome.Started ? null : tunnel.LastError,
    }, Guid.Empty, occurred));

    /// <summary>One use per invocation with a newly started rule; retries of a failed commit do not count again.</summary>
    public void RecordProfileUse(Guid operationId, Guid profileId, DateTimeOffset occurredAt)
    {
        lock (_sync)
        {
            if (_disposed || !_profileUses.Add(operationId)) { return; }
            _pending.Enqueue(new(null, profileId, occurredAt.ToUniversalTime()));
            EnsureDrainUnderLock();
        }
    }

    private Task EnsureDrainUnderLock()
    {
        if (!_draining && _pending.Count > 0) { _draining = true; _drain = Task.Run(DrainAsync); }
        return _drain ?? Task.CompletedTask;
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            PendingWrite next;
            lock (_sync)
            {
                if (!_pending.TryPeek(out next!)) { _draining = false; return; }
            }
            try { await _configuration.UpdateAsync(state => Append(state, next)).ConfigureAwait(false); }
            catch (Exception)
            {
                lock (_sync) { _draining = false; _error = UiText.Invariant("Diagnostic_TunnelActivityRecorder_RecentActivityCouldNotBeSavedYourTunnels"); }
                NotifyError();
                return;
            }
            bool recovered;
            lock (_sync) { _pending.Dequeue(); recovered = _error.Length > 0; _error = string.Empty; }
            if (recovered) { NotifyError(); }
        }
    }

    private static AppState Append(AppState state, PendingWrite next)
    {
        if (next.Activity is { } activity) { return AppendActivity(state, activity); }
        return state with
        {
            Statistics = state.Statistics with { ProfileUses = state.Statistics.ProfileUses.SetItem(next.ProfileId, checked(state.Statistics.ProfileUses.GetValueOrDefault(next.ProfileId) + 1)) },
            Profiles = state.Profiles.Select(profile => profile.Id == next.ProfileId && (profile.LastUsedAt is null || profile.LastUsedAt < next.OccurredAt)
                ? profile with { LastUsedAt = next.OccurredAt } : profile).ToImmutableArray(),
        };
    }

    private static AppState AppendActivity(AppState state, ActivityEntry activity)
    {
        if (state.RecentActivity.Any(item => item.Id == activity.Id)) { return state; }
        state = state with { RecentActivity = state.RecentActivity.Insert(0, activity)
            .OrderByDescending(item => item.OccurredAt).Take(AppState.MaximumRecentEntries).ToImmutableArray() };
        if (activity.Outcome != ActivityOutcome.Started) { return state; }
        Guid serverId = activity.Requested.ServerId;
        return state with
        {
            Statistics = state.Statistics with
            {
                TotalForwards = checked(state.Statistics.TotalForwards + 1),
                ServerForwards = state.Statistics.ServerForwards.SetItem(serverId, checked(state.Statistics.ServerForwards.GetValueOrDefault(serverId) + 1)),
            },
            Servers = state.Servers.Select(server => server.Id == serverId && (server.LastUsedAt is null || server.LastUsedAt < activity.OccurredAt)
                ? server with { LastUsedAt = activity.OccurredAt } : server).ToImmutableArray(),
        };
    }

    public async Task<bool> FlushAsync(CancellationToken cancellationToken = default)
    {
        Task work;
        lock (_sync) { work = EnsureDrainUnderLock(); }
        await work.WaitAsync(cancellationToken).ConfigureAwait(false);
        lock (_sync) { return _pending.Count == 0; }
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        if (!await FlushAsync(cancellationToken).ConfigureAwait(false)) { throw new IOException(UiText.Invariant("Diagnostic_TunnelActivityRecorder_RecentActivityIsStillWaitingToBeSaved")); }
        Dispose();
    }
    private void NotifyError()
    {
        if (ErrorChanged is not { } handlers) { return; }
        foreach (Action handler in handlers.GetInvocationList().Cast<Action>()) { try { handler(); } catch (Exception) { } }
    }
    public void Dispose() { lock (_sync) { _disposed = true; _tunnels.StateChanged -= OnStateChanged; } }
    private sealed record PendingWrite(ActivityEntry? Activity, Guid ProfileId, DateTimeOffset OccurredAt);
}

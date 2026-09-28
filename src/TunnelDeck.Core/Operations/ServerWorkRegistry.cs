using System.Collections.Immutable;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Snapshots;

namespace TunnelDeck.Core.Operations;

/// <summary>Registers accepted work before credentials or scheduling. Barriers block admission without holding a lock across user input.</summary>
public sealed class ServerWorkRegistry : IShutdownParticipant
{
    private readonly object _sync = new();
    private readonly Dictionary<Guid, ServerWorkLease> _work = [];
    private readonly HashSet<Guid> _blockedServers = [], _blockedProfiles = [];
    private readonly HashSet<Guid> _deletedServers = [], _deletedProfiles = [];
    private long _revision;
    private bool _accepting = true;
    private Task? _shutdown;
    public bool IsAccepting { get { lock (_sync) { return _accepting; } } }
    public event Action? AvailabilityChanged;

    public ServerWorkRegistrySnapshot Current
    {
        get { lock (_sync) { return new(_revision, [.. _work.Values.Select(work => work.Snapshot)]); } }
    }

    public ServerWorkLease Begin(RemoteServer server, ServerWorkKind kind, IEnumerable<RuleLaunchSnapshot>? rules = null,
        CancellationToken cancellationToken = default)
    {
        ImmutableArray<PendingForward> forwards = rules?.Select(rule => new PendingForward(rule)).ToImmutableArray() ?? [];
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_accepting) { throw new ConnectionException(ConnectionFailure.ShuttingDown); }
            if (_blockedServers.Contains(server.Id) || _deletedServers.Contains(server.Id) ||
                kind == ServerWorkKind.Profile && forwards.Any(rule => rule.Launch.Source.ProfileId is Guid profileId &&
                    (_blockedProfiles.Contains(profileId) || _deletedProfiles.Contains(profileId))))
            { throw new ConnectionException(ConnectionFailure.ServerUnavailable); }
            var work = new ServerWorkLease(this, new(Guid.NewGuid(), server, kind, forwards), cancellationToken);
            _work.Add(work.Snapshot.Id, work);
            _revision++;
            return work;
        }
    }

    internal void RequireResourceAdmission(IEnumerable<Guid> serverIds, IEnumerable<Guid> profileIds)
    {
        lock (_sync)
        {
            if (!_accepting) { throw new ConnectionException(ConnectionFailure.ShuttingDown); }
            if (serverIds.Any(id => _blockedServers.Contains(id) || _deletedServers.Contains(id)) ||
                profileIds.Any(id => _blockedProfiles.Contains(id) || _deletedProfiles.Contains(id)))
            { throw new ConnectionException(ConnectionFailure.ServerUnavailable); }
        }
    }

    internal void Validate(ServerWorkLease work, RuleLaunchSnapshot launch)
    {
        lock (_sync)
        {
            if (work.Owner != this || work.Released || !_work.ContainsKey(work.Snapshot.Id) ||
                work.Snapshot.Server.Id != launch.Server.Id ||
                !work.Snapshot.Forwards.Any(rule => rule.Launch.Requested.Id == launch.Requested.Id))
            { throw new InvalidOperationException(UiText.Invariant("Diagnostic_ServerWorkRegistry_TheForwardingOperationIsNoLongerRegistered")); }
            work.Token.ThrowIfCancellationRequested();
        }
    }

    internal void Bind(ServerWorkLease work, Guid ruleId, Guid tunnelId)
    {
        lock (_sync)
        {
            if (work.Released) { return; }
            work.Snapshot = work.Snapshot with
            { Forwards = [.. work.Snapshot.Forwards.Select(rule => rule.Launch.Requested.Id == ruleId ? rule with { TunnelId = tunnelId } : rule)] };
            _revision++;
        }
    }

    internal void FinishRule(ServerWorkLease work, Guid ruleId)
    {
        lock (_sync)
        {
            if (work.Released) { return; }
            work.Snapshot = work.Snapshot with { Forwards = [.. work.Snapshot.Forwards.Where(rule => rule.Launch.Requested.Id != ruleId)] };
            _revision++;
        }
    }

    internal void Release(ServerWorkLease work)
    {
        Task cancelling;
        lock (_sync)
        {
            if (work.Released) { return; }
            work.Released = true;
            _work.Remove(work.Snapshot.Id);
            _revision++;
            cancelling = work.Cancelling ?? Task.CompletedTask;
        }
        _ = FinishAsync(work, cancelling);
    }

    private async Task FinishAsync(ServerWorkLease work, Task cancelling)
    {
        try { await cancelling.ConfigureAwait(false); }
        catch (Exception) { /* The cancellation caller owns callback errors; completion still drains the lease. */ }
        finally
        {
            lock (_sync)
            {
                work.Cancellation.Dispose();
                work.Finished.TrySetResult();
            }
        }
    }

    internal ServerWorkBarrier Block(ResourceKind kind, IEnumerable<Guid> resourceIds)
    {
        ImmutableHashSet<Guid> ids = resourceIds.ToImmutableHashSet();
        lock (_sync)
        {
            HashSet<Guid> blocked = kind == ResourceKind.Server ? _blockedServers : _blockedProfiles;
            if (ids.Any(blocked.Contains)) { throw new InvalidOperationException(UiText.Invariant("Diagnostic_ServerWorkRegistry_ADeletionOfTheseResourcesIsAlreadyIn")); }
            blocked.UnionWith(ids);
            return new(this, kind, ids);
        }
    }

    internal async Task CancelServersAsync(ImmutableHashSet<Guid> ids)
        => await CancelWorkAsync(work => ids.Contains(work.Snapshot.Server.Id)).ConfigureAwait(false);

    private async Task CancelWorkAsync(Func<ServerWorkLease, bool> include)
    {
        Task[] cancelling, finishing;
        lock (_sync)
        {
            ServerWorkLease[] selected = _work.Values.Where(include).ToArray();
            cancelling = selected.Select(work => work.Cancelling ??= work.Cancellation.CancelAsync()).ToArray();
            finishing = selected.Select(work => work.Finished.Task).ToArray();
        }
        // Drain the actual owners as well as cancellation callbacks before releasing a deletion barrier.
        try { await Task.WhenAll(cancelling).ConfigureAwait(false); }
        finally { await Task.WhenAll(finishing).ConfigureAwait(false); }
    }

    public Task BeginShutdown()
    {
        lock (_sync)
        {
            _accepting = false;
            if (_shutdown is null || _shutdown.IsFaulted || _shutdown.IsCanceled)
            {
                _shutdown = CancelWorkAsync(_ => true);
                _ = _shutdown.ContinueWith(done => _ = done.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            return _shutdown;
        }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken) => BeginShutdown().WaitAsync(cancellationToken);

    internal void Unblock(ServerWorkBarrier barrier, bool deleted)
    {
        lock (_sync)
        {
            HashSet<Guid> blocked = barrier.Kind == ResourceKind.Server ? _blockedServers : _blockedProfiles;
            HashSet<Guid> tombstones = barrier.Kind == ResourceKind.Server ? _deletedServers : _deletedProfiles;
            if (deleted) { tombstones.UnionWith(barrier.Ids); }
            blocked.ExceptWith(barrier.Ids);
        }
        if (AvailabilityChanged is { } handlers)
        {
            foreach (Action handler in handlers.GetInvocationList().Cast<Action>())
            { try { handler(); } catch (Exception) { /* Admission observers do not change a committed deletion. */ } }
        }
    }
}

public sealed class ServerWorkLease : IDisposable
{
    internal ServerWorkRegistry Owner { get; }
    internal ServerWorkSnapshot Snapshot { get; set; }
    internal CancellationTokenSource Cancellation { get; }
    internal TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal Task? Cancelling { get; set; }
    internal bool Released { get; set; }
    public CancellationToken Token { get; }

    internal ServerWorkLease(ServerWorkRegistry owner, ServerWorkSnapshot snapshot, CancellationToken cancellationToken)
    {
        Owner = owner; Snapshot = snapshot;
        Cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Token = Cancellation.Token;
    }

    public void Bind(Guid ruleId, Guid tunnelId) => Owner.Bind(this, ruleId, tunnelId);
    public void FinishRule(Guid ruleId) => Owner.FinishRule(this, ruleId);
    public void Dispose() => Owner.Release(this);
}

internal sealed class ServerWorkBarrier(ServerWorkRegistry owner, ResourceKind kind, ImmutableHashSet<Guid> ids) : IDisposable
{
    private bool _disposed;
    public ResourceKind Kind { get; } = kind;
    public ImmutableHashSet<Guid> Ids { get; } = ids;
    public bool Deleted { get; set; }
    public void Dispose() { if (_disposed) { return; } _disposed = true; owner.Unblock(this, Deleted); }
}

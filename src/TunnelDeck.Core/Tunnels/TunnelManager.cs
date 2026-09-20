using System.Collections.Immutable;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Snapshots;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Tunnels;

/// <summary>Owns runtime instances independently of configuration and UI. Native work is drained before an entry can be forgotten.</summary>
public sealed partial class TunnelManager : IShutdownParticipant
{
    private readonly SshConnectionManager _connections;
    private readonly PortSearchPolicy _ports;
    private readonly TimeProvider _clock;
    private readonly TunnelManagerOptions _options;
    private readonly SemaphoreSlim _startSlots;
    private readonly SemaphoreSlim _cleanupSlots;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Entry> _entries = [];
    private readonly Dictionary<ForwardRequestKey, Entry> _requests = [];
    private readonly Dictionary<Guid, SemaphoreSlim> _serverGates = [];
    private readonly Dictionary<Guid, int> _serverStopBarriers = [];
    private long _revision;
    private long _nextOrder;
    private int _stopAllBarriers;
    private bool _accepting = true;
    private bool _reconnectEnabled;
    private Task? _shutdown;

    public TunnelManager(SshConnectionManager connections, PortSearchPolicy? ports = null,
        TimeProvider? clock = null, TunnelManagerOptions? options = null)
    {
        _connections = connections;
        _clock = clock ?? TimeProvider.System;
        _ports = ports ?? new(_clock);
        _options = options ?? new();
        _options.Validate();
        _startSlots = new(_options.MaximumConcurrentStarts);
        _cleanupSlots = new(_options.MaximumConcurrentCleanup);
    }

    public TunnelManagerSnapshot Current { get { lock (_sync) { return CaptureUnderLock(); } } }
    public ServerWorkRegistry Work => _connections.Work;
    /// <summary>May run on a background thread. UI subscribers dispatch and ignore older revisions.</summary>
    public event Action<TunnelManagerSnapshot>? StateChanged;

    public async Task<TunnelStartResult> StartAsync(RuleLaunchSnapshot launch, ConnectionCredentials? credentials = null,
        CancellationToken cancellationToken = default, ServerWorkLease? work = null)
    {
        ArgumentNullException.ThrowIfNull(launch);
        ConfigurationValidator.ValidateServer(launch.Server).ThrowIfInvalid();
        ConfigurationValidator.ValidateRule(launch.Requested with
        {
            AutoFindAvailablePort = launch.Requested.Mode == ForwardMode.Local && launch.Requested.AutoFindAvailablePort,
        }).ThrowIfInvalid();
        if (launch.Server.Id != launch.Requested.ServerId) { throw new ArgumentException("The rule and server snapshot must have the same server ID.", nameof(launch)); }
        launch = launch with { Server = launch.Server with { }, Requested = launch.Requested with { }, Source = launch.Source with { } };
        ForwardRequestKey key = ForwardRequestKey.FromRule(launch.Requested);
        using ServerWorkLease? ownedWork = work is null ? Work.Begin(launch.Server, ServerWorkKind.Forward, [launch], cancellationToken) : null;
        work ??= ownedWork!;
        Work.Validate(work, launch);
        using var stoppingWork = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, work.Token);
        cancellationToken = stoppingWork.Token;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entry? entry = null;
            Task<TunnelStopResult>? stopping = null;
            TunnelManagerSnapshot? changed = null;
            bool reused = false;
            lock (_sync)
            {
                RequireStartAllowed(launch.Server.Id);
                if (_requests.TryGetValue(key, out Entry? existing))
                {
                    if (existing.Value.State == TunnelState.Stopping || existing.Value.StopRequested)
                    {
                        stopping = EnsureCleanupUnderLock(existing);
                    }
                    else { entry = existing; reused = true; }
                }
                else
                {
                    entry = new(launch, key, ++_nextOrder, _clock.GetUtcNow().ToUniversalTime());
                    _entries.Add(entry.Value.Id, entry);
                    _requests.Add(key, entry);
                    if (!_serverGates.ContainsKey(launch.Server.Id)) { _serverGates.Add(launch.Server.Id, new(1, 1)); }
                    entry.ServerGate = _serverGates[launch.Server.Id];
                    // Submit the reservation before exposing Starting. A prior save may finish first, in which
                    // case the snapshot identity is checked again before any connection is opened.
                    entry.ReservationTask = _connections.ReserveAsync(launch.Server.Id, entry.Token);
                    entry.StartWork = Task.Run(() => StartCoreAsync(entry, credentials));
                    entry.Ready = Task.Run(() => CompleteStartAsync(entry));
                    changed = ChangedUnderLock();
                }
                if (entry is not null) { entry.Waiters++; }
            }
            if (entry is not null) { work.Bind(launch.Requested.Id, entry.Value.Id); }
            Notify(changed);
            if (stopping is not null)
            {
                TunnelStopResult stopped = await stopping.WaitAsync(cancellationToken).ConfigureAwait(false);
                if (!stopped.Succeeded) { return new(stopped.Tunnel!, false); }
                continue;
            }

            bool cancelled = false;
            try
            {
                await entry!.Ready.WaitAsync(cancellationToken).ConfigureAwait(false);
                lock (_sync)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (entry.Value.State == TunnelState.Running) { entry.Acknowledged = true; }
                    return new(entry.Value, reused);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { cancelled = true; throw; }
            finally
            {
                Task<TunnelStopResult>? cleanup = null;
                lock (_sync)
                {
                    entry!.Waiters--;
                    if (cancelled && entry.Waiters == 0 && !entry.Acknowledged && !entry.CleanupComplete)
                    {
                        RequestStopUnderLock(entry, TunnelState.Cancelled);
                        cleanup = EnsureCleanupUnderLock(entry);
                        changed = ChangedUnderLock();
                    }
                    else { changed = null; }
                }
                Notify(changed);
                if (cleanup is not null) { await cleanup.ConfigureAwait(false); }
            }
        }
    }

    private void RequireStartAllowed(Guid serverId)
    {
        if (!_accepting) { throw new ForwardException(new(TunnelFailureKind.Cancelled, "TunnelDeck is closing. New forwards are unavailable.")); }
        if (_stopAllBarriers > 0 || _serverStopBarriers.GetValueOrDefault(serverId) > 0)
        {
            throw new ForwardException(new(TunnelFailureKind.Cleanup, "Tunnels are being stopped. Retry starting after cleanup finishes."));
        }
    }

    private TunnelManagerSnapshot CaptureUnderLock() => new(_revision, _entries.Values.OrderBy(entry => entry.Order).Select(entry => entry.Value).ToImmutableArray());
    private TunnelManagerSnapshot ChangedUnderLock() { _revision++; return CaptureUnderLock(); }
    private void Notify(TunnelManagerSnapshot? snapshot)
    {
        if (snapshot is null || StateChanged is not { } handlers) { return; }
        foreach (Action<TunnelManagerSnapshot> handler in handlers.GetInvocationList().Cast<Action<TunnelManagerSnapshot>>())
        {
            try { handler(snapshot); }
            catch (Exception) { /* A subscriber cannot change committed runtime ownership. */ }
        }
    }

    private sealed class Entry
    {
        public RuleLaunchSnapshot Launch { get; }
        public ForwardRequestKey Key { get; }
        public long Order { get; }
        public ActiveTunnel Value { get; set; }
        public CancellationTokenSource Stop { get; set; } = new();
        public CancellationToken Token { get; set; }
        public SemaphoreSlim ServerGate { get; set; } = null!;
        public Task<ServerUseLease> ReservationTask { get; set; } = null!;
        public ServerUseLease? Reservation { get; set; }
        public SshSessionLease? Connection { get; set; }
        public IForwardHandle? Handle { get; set; }
        public Task? PendingStop { get; set; }
        public Task? PendingDispose { get; set; }
        public Task<TunnelFailure?> StartWork { get; set; } = null!;
        public Task Ready { get; set; } = null!;
        public Task<TunnelStopResult>? Cleanup { get; set; }
        public CancellationTokenRegistration Disconnected { get; set; }
        public Action<ForwardActivity>? ActivityHandler { get; set; }
        public int Waiters { get; set; }
        public bool Acknowledged { get; set; }
        public bool CleanupComplete { get; set; }
        public bool AttemptCleaned { get; set; }
        public bool RecoverRequested { get; set; }
        public TunnelState TerminalState { get; set; } = TunnelState.Failed;
        public TunnelFailure? TerminalFailure { get; set; }
        public long LastActivityRevision { get; set; } = -1;
        public long? LastDiagnosticNotification { get; set; }
        public long AcceptedBeforeAttempt { get; set; }
        public long FailedBeforeAttempt { get; set; }
        public Entry(RuleLaunchSnapshot launch, ForwardRequestKey key, long order, DateTimeOffset createdAt)
        {
            Launch = launch;
            Key = key;
            Order = order;
            Token = Stop.Token;
            Value = new() { Id = Guid.NewGuid(), Requested = launch.Requested, Source = launch.Source, CreatedAt = createdAt, Generation = 1 };
        }
    }
}

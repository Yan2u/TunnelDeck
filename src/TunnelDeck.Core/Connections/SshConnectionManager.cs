using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Connections;

/// <summary>Server identity reservations outlive individual transports. No lock is held while asking the user to trust a host.</summary>
public sealed partial class SshConnectionManager : IShutdownParticipant
{
    private readonly ConfigurationService _configuration;
    private readonly ISshSessionFactory _factory;
    private readonly ICredentialStore _credentials;
    private readonly HostKeyApprovalCoordinator _hostApproval;
    private readonly bool _networkEnabled;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, ServerState> _servers = [];
    private bool _accepting = true;
    private Task? _shutdownTask;

    public SshConnectionManager(ConfigurationService configuration, ISshSessionFactory factory,
        ICredentialStore credentials, IHostKeyStore hostKeys, IHostKeyApproval approval, bool networkEnabled = true)
    {
        _configuration = configuration;
        _factory = factory;
        _credentials = credentials;
        _hostApproval = new(hostKeys, approval);
        _networkEnabled = networkEnabled;
    }

    public event Action<Guid>? ServerUsageChanged;

    /// <summary>Includes connecting and retiring transports, not just established connections.</summary>
    public int TrackedConnectionCount { get { lock (_sync) { return _servers.Values.Sum(server => server.Connections.Count); } } }

    public bool IsServerInUse(Guid serverId)
    {
        lock (_sync) { return _servers.TryGetValue(serverId, out ServerState? state) && IsInUse(state); }
    }

    public Task<ServerUseLease> ReserveAsync(Guid serverId, CancellationToken cancellationToken = default) =>
        ReserveCoreAsync(serverId, null, cancellationToken);

    private async Task<ServerUseLease> ReserveCoreAsync(Guid serverId, RemoteServer? candidate, CancellationToken cancellationToken)
    {
        ServerState state = GetServerState(serverId);
        await state.EditGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        ServerUseLease reservation;
        try
        {
            lock (_sync)
            {
                RequireAccepting();
                if (!_networkEnabled) { throw new ConnectionException(ConnectionFailure.PreviewMode); }
                cancellationToken.ThrowIfCancellationRequested();
                RemoteServer server = candidate ?? _configuration.Current?.Servers.FirstOrDefault(item => item.Id == serverId)
                    ?? throw new ConnectionException(ConnectionFailure.ServerUnavailable);
                ConfigurationValidator.ValidateServer(server).ThrowIfInvalid();
                if (state.Reservations.Any(item => !ServerIdentity.Matches(item.Server, server)) ||
                    state.Connections.Any(item => !ServerIdentity.Matches(item.Server, server)))
                {
                    throw new ConnectionException(ConnectionFailure.ServerInUse);
                }
                reservation = new(this, server);
                state.Reservations.Add(reservation);
            }
        }
        finally { state.EditGate.Release(); }
        NotifyUsage(serverId);
        return reservation;
    }

    /// <summary>The same gate protects connection registration and the complete configuration commit.</summary>
    public async Task<ConfigurationCommitResult> SaveServerAsync(RemoteServer candidate, CredentialChange change,
        long? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        ConfigurationValidator.ValidateServer(candidate).ThrowIfInvalid();
        ServerState state = GetServerState(candidate.Id);
        await state.EditGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Task<ConfigurationCommitResult> save;
            lock (_sync)
            {
                RequireAccepting();
                RemoteServer? before = _configuration.Current?.Servers.FirstOrDefault(item => item.Id == candidate.Id);
                bool credentialChanged = change.Kind == CredentialChangeKind.Remember ||
                    (change.Kind == CredentialChangeKind.Forget && before?.CredentialReference is not null);
                if (IsInUse(state) && (before is null || !ServerIdentity.Matches(before, candidate) || credentialChanged))
                {
                    throw new ConnectionException(ConnectionFailure.ServerInUse);
                }
                if (before?.CredentialReference is not null && change.Kind == CredentialChangeKind.Keep &&
                    !ServerIdentity.SameAuthenticationTarget(before, candidate))
                {
                    throw new ConnectionException(ConnectionFailure.CredentialRequired);
                }
                // Enqueue before shutdown can start draining ConfigurationService.
                save = _configuration.SaveServerAsync(candidate, change, expectedRevision, cancellationToken);
            }
            return await save.ConfigureAwait(false);
        }
        finally { state.EditGate.Release(); }
    }

    public async Task<SshSessionLease> AcquireAsync(Guid serverId, ForwardMode mode,
        ConnectionCredentials? credentials = null, CancellationToken cancellationToken = default)
    {
        ServerUseLease reservation = await ReserveAsync(serverId, cancellationToken).ConfigureAwait(false);
        try { return await AcquireCoreAsync(reservation, mode, credentials, false, null, reservation, cancellationToken).ConfigureAwait(false); }
        catch { reservation.Dispose(); throw; }
    }

    /// <summary>For a rule that retains its reservation across reconnect attempts. Remote acquisitions are always isolated.</summary>
    public Task<SshSessionLease> AcquireAsync(ServerUseLease reservation, ForwardMode mode,
        ConnectionCredentials? credentials = null, CancellationToken cancellationToken = default) =>
        AcquireCoreAsync(reservation, mode, credentials, false, null, null, cancellationToken);

    internal Task<SshSessionLease> AcquireForReconnectAsync(ServerUseLease reservation, ForwardMode mode, CancellationToken cancellationToken) =>
        AcquireCoreAsync(reservation, mode, null, false, null, null, cancellationToken, allowHostApproval: false);

    public async Task<ConnectionTestResult> TestAsync(RemoteServer candidate, ConnectionCredentials? credentials = null,
        IProgress<ConnectionStage>? progress = null, CancellationToken cancellationToken = default)
    {
        using ServerWorkLease work = Work.Begin(candidate, ServerWorkKind.ConnectionTest, cancellationToken: cancellationToken);
        cancellationToken = work.Token;
        using ServerUseLease reservation = await ReserveCoreAsync(candidate.Id, candidate, cancellationToken).ConfigureAwait(false);
        await using SshSessionLease connection = await AcquireCoreAsync(reservation, ForwardMode.Local, credentials,
            true, progress, null, cancellationToken).ConfigureAwait(false);
        var result = new ConnectionTestResult(connection.Session.HostKey);
        progress?.Report(ConnectionStage.ClosingTest);
        // Disposal is part of test completion. This connection never enters the Local pool or creates a tunnel.
        await connection.DisposeAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    internal void ReleaseReservation(ServerUseLease reservation)
    {
        lock (_sync)
        {
            if (reservation.IsDisposed) { return; }
            reservation.IsDisposed = true;
            _servers[reservation.Server.Id].Reservations.Remove(reservation);
        }
        NotifyUsage(reservation.Server.Id);
    }

    private ServerState GetServerState(Guid serverId)
    {
        lock (_sync)
        {
            if (!_servers.TryGetValue(serverId, out ServerState? state)) { _servers.Add(serverId, state = new()); }
            return state;
        }
    }

    private void RequireAccepting()
    {
        if (!_accepting || !Work.IsAccepting) { throw new ConnectionException(ConnectionFailure.ShuttingDown); }
    }

    private static bool IsInUse(ServerState state) => state.Reservations.Count != 0 || state.Connections.Count != 0;

    private void NotifyUsage(Guid serverId)
    {
        if (ServerUsageChanged is not { } handlers) { return; }
        foreach (Action<Guid> handler in handlers.GetInvocationList().Cast<Action<Guid>>())
        {
            try { handler(serverId); }
            catch (Exception) { /* Observers cannot change ownership or the outcome of a connection. */ }
        }
    }

    private sealed class ServerState
    {
        public SemaphoreSlim EditGate { get; } = new(1, 1);
        public HashSet<ServerUseLease> Reservations { get; } = [];
        public HashSet<ConnectionEntry> Connections { get; } = [];
        public ConnectionEntry? Local { get; set; }
    }
}

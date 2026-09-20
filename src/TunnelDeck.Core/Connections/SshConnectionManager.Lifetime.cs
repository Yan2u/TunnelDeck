using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Connections;

public sealed partial class SshConnectionManager
{
    private async Task<SshSessionLease> AcquireCoreAsync(ServerUseLease reservation, ForwardMode mode,
        ConnectionCredentials? credentials, bool temporary, IProgress<ConnectionStage>? progress,
        ServerUseLease? ownedReservation, CancellationToken cancellationToken, bool allowHostApproval = true)
    {
        if (!Enum.IsDefined(mode)) { throw new ArgumentOutOfRangeException(nameof(mode)); }
        cancellationToken.ThrowIfCancellationRequested();
        ConnectionEntry entry;
        lock (_sync)
        {
            RequireAccepting();
            if (reservation.Owner != this || reservation.IsDisposed) { throw new ObjectDisposedException(nameof(reservation)); }
            ServerState state = _servers[reservation.Server.Id];
            ConnectionEntry? local = state.Local;
            if (!temporary && mode == ForwardMode.Local && local is { Retiring: false } &&
                !local.Opening.IsFaulted && !local.Opening.IsCanceled && local.Session?.Disconnected.IsCancellationRequested != true)
            {
                entry = local;
            }
            else
            {
                entry = new(reservation.Server, mode, allowHostApproval);
                state.Connections.Add(entry);
                if (!temporary && mode == ForwardMode.Local) { state.Local = entry; }
                entry.Opening = Task.Run(() => OpenEntryAsync(entry, credentials, progress));
            }
            entry.Users++;
        }
        NotifyUsage(entry.Server.Id);
        try
        {
            ISshSession session = await entry.Opening.WaitAsync(cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                RequireAccepting();
                cancellationToken.ThrowIfCancellationRequested();
                if (reservation.IsDisposed) { throw new ObjectDisposedException(nameof(reservation)); }
                if (entry.Retiring || session.Disconnected.IsCancellationRequested) { throw new ConnectionException(ConnectionFailure.ConnectionFailed); }
                return new(session, async retry =>
                {
                    try { await ReleaseEntryAsync(entry, retry).ConfigureAwait(false); }
                    finally { ownedReservation?.Dispose(); }
                }, () => { lock (_sync) { return RetireUnderLock(entry); } });
            }
        }
        catch
        {
            // A last cancelled waiter waits for the actual connect and disposal, including a late success.
            // Cleanup failures remain tracked and retryable by shutdown.
            try { await ReleaseEntryAsync(entry).ConfigureAwait(false); }
            catch (Exception) { }
            throw;
        }
    }

    private async Task<ISshSession> OpenEntryAsync(ConnectionEntry entry, ConnectionCredentials? supplied, IProgress<ConnectionStage>? progress)
    {
        ConnectionCredentials credentials = await ReadCredentialsAsync(entry.Server, supplied, progress, entry.Stop.Token).ConfigureAwait(false);
        ISshSession session = await OpenTrustedAsync(entry.Server, entry.Mode, credentials, progress, entry.Stop.Token, entry.AllowHostApproval).ConfigureAwait(false);
        lock (_sync)
        {
            entry.Session = session;
            entry.DisconnectRegistration = session.Disconnected.Register(() =>
            {
                lock (_sync) { RetireUnderLock(entry); }
                NotifyUsage(entry.Server.Id);
            });
        }
        return session;
    }

    private Task ReleaseEntryAsync(ConnectionEntry entry, bool retry = false)
    {
        lock (_sync)
        {
            if (!retry) { entry.Users--; }
            return entry.Users == 0 || entry.Retiring ? RetireUnderLock(entry) : Task.CompletedTask;
        }
    }

    private Task RetireUnderLock(ConnectionEntry entry)
    {
        if (entry.Cleaned) { return Task.CompletedTask; }
        entry.Retiring = true;
        ServerState state = _servers[entry.Server.Id];
        if (ReferenceEquals(state.Local, entry)) { state.Local = null; }
        if (entry.Cleanup is null || entry.Cleanup.IsFaulted || entry.Cleanup.IsCanceled)
        {
            entry.Cleanup = Task.Run(() => CleanupEntryAsync(entry));
            // A disconnection can initiate cleanup without a caller. Observe errors while retaining the task for retry.
            _ = entry.Cleanup.ContinueWith(task => _ = task.Exception, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        return entry.Cleanup;
    }

    private async Task CleanupEntryAsync(ConnectionEntry entry)
    {
        await entry.Stop.CancelAsync().ConfigureAwait(false);
        try { await entry.Opening.ConfigureAwait(false); }
        catch (Exception) { /* Open owns its error; the entry still owns all cleanup. */ }
        await entry.DisconnectRegistration.DisposeAsync().ConfigureAwait(false);
        if (entry.Session is not null) { await entry.Session.DisposeAsync().ConfigureAwait(false); }
        lock (_sync)
        {
            entry.Cleaned = true;
            _servers[entry.Server.Id].Connections.Remove(entry);
        }
        entry.Stop.Dispose();
        NotifyUsage(entry.Server.Id);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        Task shutdown;
        Guid[] changed;
        lock (_sync)
        {
            _accepting = false;
            changed = _servers.Keys.ToArray();
            foreach (ServerState state in _servers.Values)
            {
                foreach (ServerUseLease reservation in state.Reservations) { reservation.IsDisposed = true; }
                state.Reservations.Clear();
            }
            if (_shutdownTask is null || _shutdownTask.IsFaulted || _shutdownTask.IsCanceled)
            {
                _shutdownTask = Task.WhenAll(_servers.Values.SelectMany(state => state.Connections).Select(RetireUnderLock).ToArray());
            }
            shutdown = _shutdownTask;
        }
        foreach (Guid id in changed) { NotifyUsage(id); }
        // A timed-out close stops waiting, not cleanup. Subsequent close requests join or retry the tracked work.
        return shutdown.WaitAsync(cancellationToken);
    }

    private sealed class ConnectionEntry(RemoteServer server, ForwardMode mode, bool allowHostApproval)
    {
        public RemoteServer Server { get; } = server;
        public ForwardMode Mode { get; } = mode;
        public bool AllowHostApproval { get; } = allowHostApproval;
        public CancellationTokenSource Stop { get; } = new();
        public Task<ISshSession> Opening { get; set; } = null!;
        public ISshSession? Session { get; set; }
        public CancellationTokenRegistration DisconnectRegistration { get; set; }
        public Task? Cleanup { get; set; }
        public int Users { get; set; }
        public bool Retiring { get; set; }
        public bool Cleaned { get; set; }
    }
}

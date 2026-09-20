using System.Diagnostics;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace TunnelDeck.Infrastructure.Ssh;

/// <summary>
/// Shares local forwards or owns exactly one remote forward. A remote transport cannot be pooled:
/// SSH.NET 2026.0.0 broadcasts global replies to every Remote forwarded-port object.
/// An unknown control outcome permanently retires the transport before the gate is released.
/// </summary>
public sealed class SshControlSession : IAsyncDisposable
{
    private readonly SshClient _client;
    private readonly TimeSpan _operationBudget;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _disposeLock = new();
    private readonly HashSet<ForwardedPort> _forwards = [];
    private Task? _disposeTask;
    private Task? _inFlight;
    private int _retired;
    private bool _remoteTransport;

    public SshControlSession(SshClient client, TimeSpan operationBudget)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (operationBudget <= TimeSpan.Zero || operationBudget >= client.ConnectionInfo.Timeout)
        {
            throw new ArgumentOutOfRangeException(nameof(operationBudget),
                "The control deadline must be positive and shorter than SSH.NET's internal timeout, which can silently complete Remote Stop on timeout.");
        }
        if (client.ForwardedPorts.Any())
        {
            throw new ArgumentException("A control session must own a transport with no existing forwarded ports.", nameof(client));
        }
        _client = client;
        _operationBudget = operationBudget;
    }

    public bool IsRetired => Volatile.Read(ref _retired) != 0;

    public Task StartAsync(ForwardedPort forward, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(forward);
        return ExecuteAsync(() =>
        {
            if (_forwards.Contains(forward) && forward.IsStarted) { return; }
            bool remote = forward is ForwardedPortRemote;
            if (!remote && forward is not ForwardedPortLocal)
            {
                throw new NotSupportedException("Only TCP local and remote forwarding are supported.");
            }
            if ((_remoteTransport && _forwards.Count > 0) || (remote && _forwards.Count > 0))
            {
                throw new InvalidOperationException("Remote forwarding requires its own SSH transport. Local forwards can share a local-only transport.");
            }
            _remoteTransport = remote;
            _client.AddForwardedPort(forward);
            _forwards.Add(forward);
            try
            {
                forward.Start();
            }
            catch
            {
                if (!remote)
                {
                    _client.RemoveForwardedPort(forward);
                    _forwards.Remove(forward);
                    forward.Dispose();
                }
                // The control boundary retires remote failures before another request can be made.
                throw;
            }
        }, cancellationToken);
    }

    public Task StopAsync(ForwardedPort forward, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(forward);
        return ExecuteAsync(() =>
        {
            if (!_forwards.Contains(forward)) { return; }
            forward.Stop();
            _client.RemoveForwardedPort(forward);
            _forwards.Remove(forward);
        }, cancellationToken);
    }

    internal async Task ExecuteAsync(Action operation, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Task? work = null;
        try
        {
            long started = Stopwatch.GetTimestamp();
            lock (_disposeLock)
            {
                if (IsRetired) { throw new InvalidOperationException("The SSH session has been retired. Create a new session before issuing further requests."); }
                cancellationToken.ThrowIfCancellationRequested();
                // Retirement and dispatch share a boundary: Dispose cannot miss a newly dispatched worker.
                _inFlight = work = Task.Run(operation, CancellationToken.None);
            }
            try
            {
                await work.WaitAsync(_operationBudget, cancellationToken).ConfigureAwait(false);
                // Timer callbacks can be delayed under load. Never turn a late, silent library
                // timeout into a confirmed result merely because its timer lost that race.
                if (Stopwatch.GetElapsedTime(started) >= _operationBudget)
                {
                    throw new TimeoutException("The SSH control operation exceeded its confirmation deadline.");
                }
            }
            catch (Exception error) when (error is TimeoutException or OperationCanceledException or SshOperationTimeoutException or SshConnectionException ||
                                          (_remoteTransport && error is SshException))
            {
                // No subsequent operation may observe a reply belonging to this uncertain request.
                // Dispose is observed; this is not an abandoned background Connect/Start operation.
                try { await RetireTransportAsync().ConfigureAwait(false); }
                finally { await ObserveWorkerAsync(work).ConfigureAwait(false); }
                throw;
            }
        }
        finally
        {
            lock (_disposeLock)
            {
                // Keep a still-running worker reachable even if client disposal itself failed.
                if (ReferenceEquals(_inFlight, work) && work?.IsCompleted == true) { _inFlight = null; }
            }
            _gate.Release();
        }
    }

    private Task RetireTransportAsync()
    {
        lock (_disposeLock)
        {
            Interlocked.Exchange(ref _retired, 1);
            if (_disposeTask is null || _disposeTask.IsFaulted) { _disposeTask = Task.Run(DisposeClient); }
            return _disposeTask;
        }
    }

    private void DisposeClient()
    {
        try { _client.Dispose(); }
        catch (SshConnectionException) when (_remoteTransport)
        {
            // SSH.NET 2026.0.0 sets a Remote port to Stopping before sending its cancel
            // request. If the transport has just failed, that send can abort client
            // Dispose before the socket/timer are released. The one-port transport's
            // next Dispose skips that already-stopping port and finishes disposal.
            // A second failure still propagates and remains owned for shutdown Retry.
            _client.Dispose();
        }
    }

    private static async Task ObserveWorkerAsync(Task work)
    {
        try { await work.ConfigureAwait(false); }
        catch (Exception) { /* The operation's caller owns this error; retirement owns its completion. */ }
    }

    public async ValueTask DisposeAsync()
    {
        Task retirement = RetireTransportAsync();
        Task? work;
        lock (_disposeLock) { work = _inFlight; }
        await retirement.ConfigureAwait(false);
        if (work is not null) { await ObserveWorkerAsync(work).ConfigureAwait(false); }
    }
}

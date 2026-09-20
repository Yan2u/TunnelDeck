using TunnelDeck.Core.Operations;

namespace TunnelDeck.Core.Connections;

public sealed partial class SshConnectionManager
{
    public ServerWorkRegistry Work { get; } = new();

    internal async Task<IDisposable> LockServersAsync(IEnumerable<Guid> serverIds, CancellationToken cancellationToken)
    {
        var held = new List<SemaphoreSlim>();
        try
        {
            // Every multi-server operation uses the same order as single-server edit/reservation gates.
            foreach (Guid id in serverIds.Distinct().Order())
            {
                SemaphoreSlim gate = GetServerState(id).EditGate;
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                held.Add(gate);
            }
            lock (_sync) { RequireAccepting(); }
            return new ServerGateLease(held);
        }
        catch { foreach (SemaphoreSlim gate in held.AsEnumerable().Reverse()) { gate.Release(); } throw; }
    }

    internal async Task StopConnectionsAsync(IEnumerable<Guid> serverIds)
    {
        var ids = serverIds.ToHashSet();
        Task[] cleanup;
        lock (_sync)
        {
            cleanup = _servers.Where(pair => ids.Contains(pair.Key)).SelectMany(pair => pair.Value.Connections)
                .Select(RetireUnderLock).ToArray();
        }
        await Task.WhenAll(cleanup).ConfigureAwait(false);
        lock (_sync)
        {
            foreach (ServerState state in _servers.Where(pair => ids.Contains(pair.Key)).Select(pair => pair.Value))
            {
                foreach (ServerUseLease reservation in state.Reservations) { reservation.IsDisposed = true; }
                state.Reservations.Clear();
            }
        }
        foreach (Guid id in ids) { NotifyUsage(id); }
    }

    private sealed class ServerGateLease(List<SemaphoreSlim> gates) : IDisposable
    {
        public void Dispose() { foreach (SemaphoreSlim gate in gates.AsEnumerable().Reverse()) { gate.Release(); } }
    }
}

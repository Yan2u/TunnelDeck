using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Connections;

/// <summary>Keep this reservation across starting, running and reconnecting work, even when no transport exists.</summary>
public sealed class ServerUseLease : IDisposable
{
    internal SshConnectionManager Owner { get; }
    internal bool IsDisposed { get; set; }
    public RemoteServer Server { get; }

    internal ServerUseLease(SshConnectionManager owner, RemoteServer server) { Owner = owner; Server = server; }
    public void Dispose() => Owner.ReleaseReservation(this);
}

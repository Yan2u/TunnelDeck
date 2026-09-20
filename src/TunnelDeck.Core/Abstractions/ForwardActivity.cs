using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Abstractions;

/// <summary>Observed listener activity. An accepted client does not prove that its target is reachable.</summary>
public sealed record ForwardActivity
{
    /// <summary>Monotonically increasing per handle; concurrent observers may receive older snapshots last.</summary>
    public long Revision { get; init; }
    public long AcceptedConnections { get; init; }
    public long FailedConnections { get; init; }
    public DateTimeOffset? LastRequestAt { get; init; }
    public DateTimeOffset? LastFailureAt { get; init; }
    public TunnelFailure? LastFailure { get; init; }
    public bool ListenerStopped { get; init; }
}

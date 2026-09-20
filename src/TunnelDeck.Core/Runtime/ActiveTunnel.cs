using System.Text.Json.Serialization;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Snapshots;

namespace TunnelDeck.Core.Runtime;

/// <summary>Runtime state snapshot. The engine owns handles separately; AppState never contains this type.</summary>
public sealed record ActiveTunnel
{
    public Guid Id { get; init; }
    public ForwardRule Requested { get; init; } = new();
    public ForwardRoute? Actual { get; init; }
    public ForwardSourceSnapshot Source { get; init; } = new();
    public TunnelState State { get; init; } = TunnelState.Starting;
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public DateTimeOffset? LastConnectionAt { get; init; }
    public TunnelFailure? LastError { get; init; }
    public long Generation { get; init; }
    public int ReconnectAttempt { get; init; }
    public int ReconnectCount { get; init; }
    public DateTimeOffset? NextReconnectAt { get; init; }
    public bool StopRequested { get; init; }
    public PortAdjustmentReason? AdjustmentReason { get; init; }
    public int BindAttempts { get; init; }
    public PortSearchStopReason? PortSearchStopReason { get; init; }
    public long AcceptedConnections { get; init; }
    public long RequestFailureCount { get; init; }
    public DateTimeOffset? LastRequestAt { get; init; }
    public DateTimeOffset? LastRequestFailureAt { get; init; }
    public TunnelFailure? LastRequestError { get; init; }

    [JsonIgnore]
    public Guid ServerId => Requested.ServerId;

    [JsonIgnore]
    public bool IsRunning => State == TunnelState.Running;
}

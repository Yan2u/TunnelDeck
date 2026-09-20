using System.Text.Json.Serialization;
using TunnelDeck.Core.Snapshots;

namespace TunnelDeck.Core.Models;

public enum ActivityOutcome
{
    Started,
    Stopped,
    Failed,
    Cancelled,
}

/// <summary>Historical values, never references to mutable resources or live network handles.</summary>
public sealed record ActivityEntry
{
    [JsonRequired] public Guid Id { get; init; }
    [JsonRequired] public DateTimeOffset OccurredAt { get; init; }
    [JsonRequired] public ActivityOutcome Outcome { get; init; }
    [JsonRequired] public ForwardRule Requested { get; init; } = new();
    public ForwardRoute? Actual { get; init; }
    [JsonRequired] public ForwardSourceSnapshot Source { get; init; } = new();
    public TunnelFailure? Error { get; init; }
}

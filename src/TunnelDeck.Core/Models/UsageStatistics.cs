using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace TunnelDeck.Core.Models;

/// <summary>Cumulative counts survive recent-activity trimming and resource deletion.</summary>
public sealed record UsageStatistics
{
    [JsonRequired] public long TotalForwards { get; init; }
    [JsonRequired] public ImmutableDictionary<Guid, long> ServerForwards { get; init; } = ImmutableDictionary<Guid, long>.Empty;
    [JsonRequired] public ImmutableDictionary<Guid, long> ProfileUses { get; init; } = ImmutableDictionary<Guid, long>.Empty;
}

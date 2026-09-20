using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace TunnelDeck.Core.Models;

public sealed record ForwardProfile
{
    [JsonRequired] public Guid Id { get; init; }
    [JsonRequired] public string Name { get; init; } = string.Empty;
    [JsonRequired] public ImmutableArray<ForwardRule> Rules { get; init; } = [];
    public DateTimeOffset? LastUsedAt { get; init; }
}

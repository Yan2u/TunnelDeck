using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace TunnelDeck.Core.Models;

public sealed record ResourceOrder
{
    [JsonRequired] public ImmutableArray<Guid> PinnedProfileIds { get; init; } = [];
    [JsonRequired] public ImmutableArray<Guid> OtherProfileIds { get; init; } = [];
    [JsonRequired] public ImmutableArray<Guid> PinnedServerIds { get; init; } = [];
    [JsonRequired] public ImmutableArray<Guid> OtherServerIds { get; init; } = [];
}

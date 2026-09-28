using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace TunnelDeck.Core.Models;

/// <summary>Persistence root: deliberately excludes active tunnels, credentials and host trust.</summary>
public sealed record AppState
{
    public const int CurrentSchemaVersion = 3;
    public const int MaximumRecentEntries = 200;

    [JsonRequired]
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    [JsonRequired]
    public long Revision { get; init; }

    [JsonRequired]
    public ImmutableArray<RemoteServer> Servers { get; init; } = [];

    [JsonRequired]
    public ImmutableArray<ForwardProfile> Profiles { get; init; } = [];

    [JsonRequired]
    public ResourceOrder Order { get; init; } = new();

    [JsonRequired]
    public AppSettings Settings { get; init; } = new();
    [JsonRequired]
    public ImmutableArray<ActivityEntry> RecentActivity { get; init; } = [];

    [JsonRequired]
    public UsageStatistics Statistics { get; init; } = new();
}

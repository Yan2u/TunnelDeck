using System.Text.Json.Serialization;

namespace TunnelDeck.Core.Models;

public sealed record ForwardRule
{
    public const string DefaultHost = "127.0.0.1";

    [JsonRequired] public Guid Id { get; init; }
    [JsonRequired] public Guid ServerId { get; init; }
    [JsonRequired] public ForwardMode Mode { get; init; }
    [JsonRequired] public string LocalHost { get; init; } = DefaultHost;
    [JsonRequired] public int LocalPort { get; init; }
    [JsonRequired] public string RemoteHost { get; init; } = DefaultHost;
    [JsonRequired] public int RemotePort { get; init; }
    [JsonRequired] public bool AutoFindAvailablePort { get; init; }

    public ForwardRoute GetRoute() => new(new(LocalHost, LocalPort), new(RemoteHost, RemotePort));
}

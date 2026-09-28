using System.Collections.Immutable;
using System.Text.Json.Serialization;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Transfer;

/// <summary>Portable resource document. Never carries secrets, credential references, host trust, usage or runtime state.</summary>
public sealed record TunnelDeckExport
{
    public const int CurrentSchemaVersion = 1;
    public const string Kind = "tunneldeck-export";

    [JsonRequired] public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    [JsonRequired] public string Type { get; init; } = Kind;
    public DateTimeOffset? ExportedAt { get; init; }
    [JsonRequired] public ImmutableArray<ExportedServer> Servers { get; init; } = [];
    [JsonRequired] public ImmutableArray<ExportedProfile> Profiles { get; init; } = [];
    [JsonRequired] public ResourceOrder Order { get; init; } = new();
}

public sealed record ExportedServer
{
    [JsonRequired] public Guid Id { get; init; }
    public string? DisplayName { get; init; }
    [JsonRequired] public string Host { get; init; } = string.Empty;
    [JsonRequired] public int Port { get; init; } = 22;
    [JsonRequired] public string Username { get; init; } = string.Empty;
    [JsonRequired] public AuthenticationMethod AuthMethod { get; init; }
    /// <summary>Only a path hint; the file may not exist on the importing computer.</summary>
    public string? IdentityFilePath { get; init; }

    public static ExportedServer From(RemoteServer server) => new()
    {
        Id = server.Id, DisplayName = server.DisplayName, Host = server.Host, Port = server.Port,
        Username = server.Username, AuthMethod = server.AuthMethod, IdentityFilePath = server.IdentityFilePath,
    };

    public RemoteServer ToServer(Guid id) => new()
    {
        Id = id, DisplayName = string.IsNullOrWhiteSpace(DisplayName) ? null : DisplayName, Host = Host, Port = Port,
        Username = Username, AuthMethod = AuthMethod, IdentityFilePath = string.IsNullOrWhiteSpace(IdentityFilePath) ? null : IdentityFilePath,
    };
}

public sealed record ExportedProfile
{
    [JsonRequired] public Guid Id { get; init; }
    [JsonRequired] public string Name { get; init; } = string.Empty;
    [JsonRequired] public ImmutableArray<ExportedRule> Rules { get; init; } = [];

    public static ExportedProfile From(ForwardProfile profile) => new()
    {
        Id = profile.Id, Name = profile.Name, Rules = profile.Rules.Select(ExportedRule.From).ToImmutableArray(),
    };
}

public sealed record ExportedRule
{
    [JsonRequired] public Guid Id { get; init; }
    [JsonRequired] public Guid ServerId { get; init; }
    [JsonRequired] public ForwardMode Mode { get; init; }
    [JsonRequired] public string LocalHost { get; init; } = ForwardRule.DefaultHost;
    [JsonRequired] public int LocalPort { get; init; }
    [JsonRequired] public string RemoteHost { get; init; } = ForwardRule.DefaultHost;
    [JsonRequired] public int RemotePort { get; init; }
    [JsonRequired] public bool AutoFindAvailablePort { get; init; }

    public static ExportedRule From(ForwardRule rule) => new()
    {
        Id = rule.Id, ServerId = rule.ServerId, Mode = rule.Mode, LocalHost = rule.LocalHost, LocalPort = rule.LocalPort,
        RemoteHost = rule.RemoteHost, RemotePort = rule.RemotePort, AutoFindAvailablePort = rule.AutoFindAvailablePort,
    };

    public ForwardRule ToRule(Guid id, Guid serverId) => new()
    {
        Id = id, ServerId = serverId, Mode = Mode, LocalHost = LocalHost, LocalPort = LocalPort,
        RemoteHost = RemoteHost, RemotePort = RemotePort, AutoFindAvailablePort = AutoFindAvailablePort,
    };
}

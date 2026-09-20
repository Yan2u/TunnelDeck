using System.Text.Json.Serialization;

namespace TunnelDeck.Core.Models;

/// <summary>Persisted connection configuration. Secrets and host trust live in separate stores.</summary>
public sealed record RemoteServer
{
    [JsonRequired] public Guid Id { get; init; }
    public string? DisplayName { get; init; }
    [JsonRequired] public string Host { get; init; } = string.Empty;
    [JsonRequired] public int Port { get; init; } = 22;
    [JsonRequired] public string Username { get; init; } = string.Empty;
    [JsonRequired] public AuthenticationMethod AuthMethod { get; init; }
    public string? IdentityFilePath { get; init; }
    public Guid? CredentialReference { get; init; }
    public DateTimeOffset? LastUsedAt { get; init; }
}

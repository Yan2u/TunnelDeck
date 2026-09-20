using System.Text.Json.Serialization;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Security;

namespace TunnelDeck.Core.Abstractions;

public sealed class ConnectionCredentials(string? secret)
{
    [JsonIgnore]
    public string? Secret { get; } = secret;

    public override string ToString() => "Connection credentials (redacted)";
}

public sealed record SshSessionRequest(RemoteServer Server, ForwardMode Mode,
    ConnectionCredentials Credentials, HostKeyIdentity? TrustedKey);

public interface ISshSessionFactory
{
    /// <summary>Creates a Local-only or single-Remote transport; the Server coordinator owns pooling and identity checks.</summary>
    Task<ISshSession> OpenAsync(SshSessionRequest request, CancellationToken cancellationToken = default);
}

public interface ISshSession : IAsyncDisposable
{
    Guid ServerId { get; }
    ForwardMode Mode { get; }
    HostKeyIdentity HostKey { get; }
    CancellationToken Disconnected { get; }
    Task<IForwardHandle> StartAsync(ForwardRule rule, CancellationToken cancellationToken = default);
}

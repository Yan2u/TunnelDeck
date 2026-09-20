namespace TunnelDeck.Core.Security;

/// <summary>Stored outside AppState; importing server configuration never creates trust.</summary>
public sealed record TrustedHostKey(HostKeyIdentity Identity, DateTimeOffset TrustedAt);

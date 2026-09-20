using TunnelDeck.Core.Security;

namespace TunnelDeck.Core.Connections;

public enum ConnectionFailure
{
    CredentialRequired,
    CredentialUnavailable,
    AuthenticationFailed,
    KeyFileUnreadable,
    KeyUnlockFailed,
    HostKeyDeclined,
    HostKeyChanged,
    TrustStoreUnavailable,
    NameResolutionFailed,
    TimedOut,
    ConnectionFailed,
    ServerInUse,
    ServerUnavailable,
    ShuttingDown,
    PreviewMode,
}

/// <summary>Only fixed, safe messages cross the transport boundary. No library exception or secret is retained.</summary>
public class ConnectionException(ConnectionFailure failure) : Exception(MessageFor(failure))
{
    public ConnectionFailure Failure { get; } = failure;

    private static string MessageFor(ConnectionFailure failure) => failure switch
    {
        ConnectionFailure.CredentialRequired => "Enter a password or the passphrase for your encrypted private key, then try again.",
        ConnectionFailure.CredentialUnavailable => "The saved credential could not be read. Enter it again to connect.",
        ConnectionFailure.AuthenticationFailed => "SSH authentication failed. Check the username and password or the authorized private key.",
        ConnectionFailure.KeyFileUnreadable => "The private-key file could not be read. Check its path and file permissions.",
        ConnectionFailure.KeyUnlockFailed => "The private key could not be opened. Check the file format and passphrase.",
        ConnectionFailure.HostKeyDeclined => "The host key was not trusted. The connection was cancelled before authentication.",
        ConnectionFailure.HostKeyChanged => "The server's host key differs from the trusted key. Connection refused. Verify the change with your server administrator.",
        ConnectionFailure.TrustStoreUnavailable => "Saved host trust could not be read or written. Check the TunnelDeck data folder and try again.",
        ConnectionFailure.NameResolutionFailed => "The hostname could not be resolved. Check the host and your network connection.",
        ConnectionFailure.TimedOut => "The SSH connection timed out. Check the host, SSH port and network, then retry.",
        ConnectionFailure.ServerInUse => "This server is in use. Stop its tunnels and pending connections before changing the host, port, username, authentication or credentials. You can still change its display name.",
        ConnectionFailure.ServerUnavailable => "The server is no longer available. Select a saved server and try again.",
        ConnectionFailure.ShuttingDown => "TunnelDeck is closing. New connections and server edits are unavailable.",
        ConnectionFailure.PreviewMode => "Connection testing is disabled for preview data. Open TunnelDeck normally to connect to a real server.",
        _ => "The SSH connection failed. Check the server and network, then retry.",
    };
}

/// <summary>The transport rejected this exact key before authentication; it is not an implicit trust decision.</summary>
public sealed class HostKeyNotTrustedException(HostKeyIdentity presentedKey) : Exception("The SSH host key was not trusted.")
{
    public HostKeyIdentity PresentedKey { get; } = presentedKey;
}

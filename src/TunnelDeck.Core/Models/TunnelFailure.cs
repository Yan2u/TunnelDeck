namespace TunnelDeck.Core.Models;

public enum TunnelFailureKind
{
    Unknown,
    Input,
    Credentials,
    UnknownHostKey,
    ChangedHostKey,
    Connectivity,
    LocalAddressInUse,
    LocalAccessDenied,
    RemoteListenFailure,
    TimeoutUnknownOutcome,
    Cancelled,
    Storage,
    ConnectionRequest,
    Cleanup,
}

/// <param name="Message">User-safe explanation; adapters must not include passwords or key contents.</param>
public sealed record TunnelFailure(TunnelFailureKind Kind, string Message);

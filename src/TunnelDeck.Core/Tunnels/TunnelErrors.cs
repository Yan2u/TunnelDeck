using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Tunnels;

internal static class TunnelErrors
{
    public static TunnelFailure From(Exception error) => error switch
    {
        ForwardException forward => forward.Failure,
        DomainValidationException => new(TunnelFailureKind.Input, "Check the forwarding rule and server fields."),
        ConnectionException connection => new(connection.Failure switch
        {
            ConnectionFailure.CredentialRequired or ConnectionFailure.CredentialUnavailable or ConnectionFailure.AuthenticationFailed or ConnectionFailure.KeyFileUnreadable or ConnectionFailure.KeyUnlockFailed => TunnelFailureKind.Credentials,
            ConnectionFailure.HostKeyChanged => TunnelFailureKind.ChangedHostKey,
            ConnectionFailure.HostKeyDeclined => TunnelFailureKind.UnknownHostKey,
            ConnectionFailure.TrustStoreUnavailable => TunnelFailureKind.Storage,
            ConnectionFailure.ServerInUse or ConnectionFailure.ServerUnavailable or ConnectionFailure.PreviewMode => TunnelFailureKind.Input,
            ConnectionFailure.ShuttingDown => TunnelFailureKind.Cancelled,
            _ => TunnelFailureKind.Connectivity,
        }, connection.Message),
        OperationCanceledException => new(TunnelFailureKind.Cancelled, "The forwarding request was cancelled."),
        TimeoutException => UnknownOutcome,
        _ => new(TunnelFailureKind.Unknown, "The forwarding operation could not complete. Check the server and retry."),
    };

    public static TunnelFailure UnknownOutcome => new(TunnelFailureKind.TimeoutUnknownOutcome,
        "The listening operation did not finish within its budget. Its SSH transport has been retired and late work is being cleaned up.");
    public static TunnelFailure Disconnected => new(TunnelFailureKind.Connectivity, "The SSH connection ended. This listener is being stopped.");
    public static TunnelFailure Cleanup => new(TunnelFailureKind.Cleanup, "Cleanup did not finish. Retry Stop; this tunnel still reserves its server and request until cleanup succeeds.");
}

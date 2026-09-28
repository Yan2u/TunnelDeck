using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Tunnels;

internal static class TunnelErrors
{
    public static TunnelFailure From(Exception error) => error switch
    {
        ForwardException forward => forward.Failure,
        DomainValidationException => new(TunnelFailureKind.Input, UiText.Invariant("Diagnostic_TunnelErrors_CheckTheForwardingRuleAndServerFields")),
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
        OperationCanceledException => new(TunnelFailureKind.Cancelled, UiText.Invariant("Diagnostic_TunnelErrors_TheForwardingRequestWasCancelled")),
        TimeoutException => UnknownOutcome,
        _ => new(TunnelFailureKind.Unknown, UiText.Invariant("Diagnostic_TunnelErrors_TheForwardingOperationCouldNotCompleteCheckThe")),
    };

    public static TunnelFailure UnknownOutcome => new(TunnelFailureKind.TimeoutUnknownOutcome,
        UiText.Invariant("Diagnostic_TunnelErrors_TheListeningOperationDidNotFinishWithinIts"));
    public static TunnelFailure Disconnected => new(TunnelFailureKind.Connectivity, UiText.Invariant("Diagnostic_TunnelErrors_TheSSHConnectionEndedThisListenerIsBeing"));
    public static TunnelFailure Cleanup => new(TunnelFailureKind.Cleanup, UiText.Invariant("Diagnostic_TunnelErrors_CleanupDidNotFinishRetryStopThisTunnel"));
}

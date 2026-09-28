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
        ConnectionFailure.CredentialRequired => UiText.Invariant("Diagnostic_ConnectionException_EnterAPasswordOrThePassphraseForYour"),
        ConnectionFailure.CredentialUnavailable => UiText.Invariant("Diagnostic_ConnectionException_TheSavedCredentialCouldNotBeReadEnter"),
        ConnectionFailure.AuthenticationFailed => UiText.Invariant("Diagnostic_ConnectionException_SSHAuthenticationFailedCheckTheUsernameAndPassword"),
        ConnectionFailure.KeyFileUnreadable => UiText.Invariant("Diagnostic_ConnectionException_ThePrivateKeyFileCouldNotBeRead"),
        ConnectionFailure.KeyUnlockFailed => UiText.Invariant("Diagnostic_ConnectionException_ThePrivateKeyCouldNotBeOpenedCheck"),
        ConnectionFailure.HostKeyDeclined => UiText.Invariant("Diagnostic_ConnectionException_TheHostKeyWasNotTrustedTheConnection"),
        ConnectionFailure.HostKeyChanged => UiText.Invariant("Diagnostic_ConnectionException_TheServerSHostKeyDiffersFromThe"),
        ConnectionFailure.TrustStoreUnavailable => UiText.Invariant("Diagnostic_ConnectionException_SavedHostTrustCouldNotBeReadOr"),
        ConnectionFailure.NameResolutionFailed => UiText.Invariant("Diagnostic_ConnectionException_TheHostnameCouldNotBeResolvedCheckThe"),
        ConnectionFailure.TimedOut => UiText.Invariant("Diagnostic_ConnectionException_TheSSHConnectionTimedOutCheckTheHost"),
        ConnectionFailure.ServerInUse => UiText.Invariant("Diagnostic_ConnectionException_ThisServerIsInUseStopItsTunnels"),
        ConnectionFailure.ServerUnavailable => UiText.Invariant("Diagnostic_ConnectionException_TheServerIsNoLongerAvailableSelectA"),
        ConnectionFailure.ShuttingDown => UiText.Invariant("Diagnostic_ConnectionException_TunnelDeckIsClosingNewConnectionsAndServerEdits"),
        ConnectionFailure.PreviewMode => UiText.Invariant("Diagnostic_ConnectionException_ConnectionTestingIsDisabledForPreviewDataOpen"),
        _ => UiText.Invariant("Diagnostic_ConnectionException_TheSSHConnectionFailedCheckTheServerAnd"),
    };
}

/// <summary>The transport rejected this exact key before authentication; it is not an implicit trust decision.</summary>
public sealed class HostKeyNotTrustedException(HostKeyIdentity presentedKey) : Exception(UiText.Invariant("Diagnostic_ConnectionException_TheSSHHostKeyWasNotTrusted"))
{
    public HostKeyIdentity PresentedKey { get; } = presentedKey;
}

using System.Net.Sockets;
using Renci.SshNet.Common;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Infrastructure.Ssh;

internal static class ForwardFailureMapper
{
    public static ForwardException Start(Exception error, ForwardMode mode) => new(error switch
    {
        SocketException socket when mode == ForwardMode.Local && socket.SocketErrorCode == SocketError.AddressAlreadyInUse =>
            new(TunnelFailureKind.LocalAddressInUse, UiText.Invariant("Diagnostic_ForwardFailureMapper_TheLocalListeningPortIsAlreadyInUse")),
        SocketException socket when mode == ForwardMode.Local && socket.SocketErrorCode == SocketError.AccessDenied =>
            new(TunnelFailureKind.LocalAccessDenied, UiText.Invariant("Diagnostic_ForwardFailureMapper_WindowsDeniedAccessToTheLocalListeningAddress")),
        TimeoutException or SshOperationTimeoutException =>
            new(TunnelFailureKind.TimeoutUnknownOutcome, UiText.Invariant("Diagnostic_ForwardFailureMapper_TheListeningRequestWasNotConfirmedBeforeIts")),
        SshConnectionException => new(TunnelFailureKind.Connectivity, UiText.Invariant("Diagnostic_ForwardFailureMapper_TheSSHConnectionEndedWhileStartingTheListener")),
        // In the pinned ForwardedPortRemote.StartPort implementation this exact type represents SSH_MSG_REQUEST_FAILURE.
        // The refusal reason (occupation, AllowTcpForwarding, PermitListen, etc.) is not available.
        SshException when mode == ForwardMode.Remote && error.GetType() == typeof(SshException) =>
            new(TunnelFailureKind.RemoteListenFailure, UiText.Invariant("Diagnostic_ForwardFailureMapper_TheSSHServerRefusedRemoteListeningCheckThe")),
        _ => new(TunnelFailureKind.Connectivity, UiText.Invariant("Diagnostic_ForwardFailureMapper_TheListenerCouldNotBeStartedCheckThe")),
    });

    public static TunnelFailure Request() => new(TunnelFailureKind.ConnectionRequest,
        UiText.Invariant("Diagnostic_ForwardFailureMapper_AForwardedConnectionFailedCheckTheTargetService"));
}

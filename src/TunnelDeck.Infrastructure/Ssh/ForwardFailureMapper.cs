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
            new(TunnelFailureKind.LocalAddressInUse, "The local listening port is already in use."),
        SocketException socket when mode == ForwardMode.Local && socket.SocketErrorCode == SocketError.AccessDenied =>
            new(TunnelFailureKind.LocalAccessDenied, "Windows denied access to the local listening address or port. Choose another port or check local policy."),
        TimeoutException or SshOperationTimeoutException =>
            new(TunnelFailureKind.TimeoutUnknownOutcome, "The listening request was not confirmed before its deadline. Its SSH transport has been retired."),
        SshConnectionException => new(TunnelFailureKind.Connectivity, "The SSH connection ended while starting the listener."),
        // In the pinned ForwardedPortRemote.StartPort implementation this exact type represents SSH_MSG_REQUEST_FAILURE.
        // The refusal reason (occupation, AllowTcpForwarding, PermitListen, etc.) is not available.
        SshException when mode == ForwardMode.Remote && error.GetType() == typeof(SshException) =>
            new(TunnelFailureKind.RemoteListenFailure, "The SSH server refused remote listening. Check the requested address, port and forwarding policy; the server did not provide a specific reason."),
        _ => new(TunnelFailureKind.Connectivity, "The listener could not be started. Check the addresses and SSH connection."),
    });

    public static TunnelFailure Request() => new(TunnelFailureKind.ConnectionRequest,
        "A forwarded connection failed. Check the target service and the connecting client. The listener can accept another request.");
}

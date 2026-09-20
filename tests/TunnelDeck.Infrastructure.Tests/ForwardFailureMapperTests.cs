using System.Net.Sockets;
using Renci.SshNet.Common;
using TunnelDeck.Core.Models;
using TunnelDeck.Infrastructure.Ssh;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class ForwardFailureMapperTests
{
    [Theory]
    [InlineData(ForwardMode.Local, SocketError.AddressAlreadyInUse, TunnelFailureKind.LocalAddressInUse)]
    [InlineData(ForwardMode.Local, SocketError.AccessDenied, TunnelFailureKind.LocalAccessDenied)]
    [InlineData(ForwardMode.Local, SocketError.ConnectionRefused, TunnelFailureKind.Connectivity)]
    [InlineData(ForwardMode.Remote, SocketError.AddressAlreadyInUse, TunnelFailureKind.Connectivity)]
    [InlineData(ForwardMode.Remote, SocketError.AccessDenied, TunnelFailureKind.Connectivity)]
    public void OnlyConfirmedLocalBindOccupationCanEnableSearch(ForwardMode mode, SocketError socketError, TunnelFailureKind expected)
    {
        Assert.Equal(expected, ForwardFailureMapper.Start(new SocketException((int)socketError), mode).Failure.Kind);
    }

    [Fact]
    public void RemoteRefusalIsDifferentFromUnknownOutcomeAndNeverClaimsOccupation()
    {
        var refusal = ForwardFailureMapper.Start(new SshException("Do not expose this library message."), ForwardMode.Remote);
        Assert.Equal(TunnelFailureKind.RemoteListenFailure, refusal.Failure.Kind);
        Assert.DoesNotContain("Do not expose", refusal.ToString());
        Assert.DoesNotContain("already in use", refusal.Message);
        Assert.Equal(TunnelFailureKind.TimeoutUnknownOutcome, ForwardFailureMapper.Start(new SshOperationTimeoutException(), ForwardMode.Remote).Failure.Kind);
        Assert.Equal(TunnelFailureKind.Connectivity, ForwardFailureMapper.Start(new SshConnectionException(), ForwardMode.Remote).Failure.Kind);
    }
}

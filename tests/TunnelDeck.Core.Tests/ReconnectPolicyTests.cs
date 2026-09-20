using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Tests;

public sealed class ReconnectPolicyTests
{
    [Theory]
    [InlineData(0, 800)]
    [InlineData(0.5, 1000)]
    [InlineData(1, 1200)]
    public void JitterIsBoundedAndNeitherDelayNorWindowCanBeExceeded(double random, int firstMilliseconds)
    {
        var policy = new ReconnectPolicy(random: () => random);
        Assert.Equal(TimeSpan.FromMilliseconds(firstMilliseconds), policy.Delay(0, policy.RecoveryWindow));
        Assert.InRange(policy.Delay(20, policy.RecoveryWindow), TimeSpan.FromSeconds(24), TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromMilliseconds(100), policy.Delay(20, TimeSpan.FromMilliseconds(100)));
        Assert.Equal(TimeSpan.Zero, policy.Delay(0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData(TunnelFailureKind.Connectivity, true)]
    [InlineData(TunnelFailureKind.TimeoutUnknownOutcome, true)]
    [InlineData(TunnelFailureKind.Credentials, false)]
    [InlineData(TunnelFailureKind.ChangedHostKey, false)]
    [InlineData(TunnelFailureKind.UnknownHostKey, false)]
    [InlineData(TunnelFailureKind.RemoteListenFailure, false)]
    [InlineData(TunnelFailureKind.LocalAccessDenied, false)]
    [InlineData(TunnelFailureKind.Input, false)]
    [InlineData(TunnelFailureKind.Storage, false)]
    public void OnlyTransientFailuresAreRetried(TunnelFailureKind kind, bool expected) =>
        Assert.Equal(expected, ReconnectPolicy.CanRetry(new(kind, "safe test failure")));
}

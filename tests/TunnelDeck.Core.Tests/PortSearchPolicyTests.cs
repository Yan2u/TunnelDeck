using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Tests;

public sealed class PortSearchPolicyTests
{
    public static IEnumerable<object[]> NonRetryableFailures => Enum.GetValues<TunnelFailureKind>()
        .Where(kind => kind != TunnelFailureKind.LocalAddressInUse).Select(kind => new object[] { kind });
    public static IEnumerable<object[]> RemoteFailures => Enum.GetValues<TunnelFailureKind>()
        .SelectMany(kind => new[] { new object[] { kind, false }, new object[] { kind, true } });

    [Theory]
    [InlineData(ForwardMode.Local)]
    [InlineData(ForwardMode.Remote)]
    public void ListenerAndTargetFollowTheActualDirection(ForwardMode mode)
    {
        ForwardRule rule = DomainData.Rule(mode: mode) with { LocalHost = "127.0.0.1", RemoteHost = "::1" };
        ForwardRoute route = rule.GetRoute();
        Assert.Equal(mode == ForwardMode.Local ? new("127.0.0.1", 3000) : new NetworkEndpoint("::1", 5432), route.Listener(mode));
        Assert.Equal(mode == ForwardMode.Local ? new("::1", 5432) : new NetworkEndpoint("127.0.0.1", 3000), route.Target(mode));
    }

    [Fact]
    public void TwentyAttemptsIncludeRequestedAndNeverChangeTheTargetOrSavedRule()
    {
        ForwardRule requested = DomainData.Rule() with { RemoteHost = "db.internal" };
        PortSearch search = new PortSearchPolicy(new ManualTimeProvider()).Begin(requested);
        var attempts = new List<PortSearchAttempt>();
        PortSearchAttempt? attempt = search.Next();
        while (attempt is not null)
        {
            attempts.Add(attempt);
            attempt = search.Next(TunnelFailureKind.LocalAddressInUse);
        }
        Assert.Equal(Enumerable.Range(3000, 20), attempts.Select(value => value.Candidate.LocalPort));
        Assert.Equal(Enumerable.Range(1, 20), attempts.Select(value => value.Number));
        Assert.All(attempts, value =>
        {
            Assert.Equal(requested.GetRoute().Target(ForwardMode.Local), value.Candidate.GetRoute().Target(ForwardMode.Local));
            Assert.Equal(requested.LocalHost, value.Candidate.LocalHost);
            Assert.Equal(requested.ServerId, value.Candidate.ServerId);
        });
        Assert.Equal(3000, requested.LocalPort);
        Assert.Equal(requested, search.Requested);
        Assert.Equal(20, search.AttemptsIssued);
        Assert.Equal(PortSearchStopReason.AttemptLimitReached, search.StopReason);
        Assert.Null(search.Next(TunnelFailureKind.LocalAddressInUse));
    }

    [Theory]
    [InlineData(65535, 1)]
    [InlineData(65534, 2)]
    [InlineData(65520, 16)]
    public void PortLimitNeverWraps(int requested, int count)
    {
        PortSearch search = new PortSearchPolicy(new ManualTimeProvider()).Begin(DomainData.Rule(localPort: requested));
        var ports = new List<int>();
        for (PortSearchAttempt? attempt = search.Next(); attempt is not null; attempt = search.Next(TunnelFailureKind.LocalAddressInUse))
        {
            ports.Add(attempt.Candidate.LocalPort);
        }
        Assert.Equal(Enumerable.Range(requested, count), ports);
        Assert.Equal(PortSearchStopReason.PortLimitReached, search.StopReason);
    }

    [Theory]
    [MemberData(nameof(NonRetryableFailures))]
    public void LocalErrorsOtherThanConfirmedOccupationDoNotAdvance(TunnelFailureKind failure)
    {
        PortSearch search = new PortSearchPolicy().Begin(DomainData.Rule());
        Assert.NotNull(search.Next());
        Assert.Null(search.Next(failure));
        Assert.Equal(1, search.AttemptsIssued);
        Assert.Equal(PortSearchStopReason.NonRetryableFailure, search.StopReason);
    }

    [Theory]
    [MemberData(nameof(RemoteFailures))]
    public void RemoteNeverIncrementsEvenWithAMalformedAutoFlag(TunnelFailureKind failure, bool flag)
    {
        ForwardRule requested = DomainData.Rule(mode: ForwardMode.Remote, localPort: 1234, remotePort: 8000) with { AutoFindAvailablePort = flag };
        PortSearch search = new PortSearchPolicy().Begin(requested);
        PortSearchAttempt initial = Assert.IsType<PortSearchAttempt>(search.Next());
        Assert.False(initial.Candidate.AutoFindAvailablePort);
        Assert.Equal(1234, initial.Candidate.LocalPort);
        Assert.Equal(8000, initial.Candidate.RemotePort);
        Assert.Null(search.Next(failure));
        Assert.Equal(1, search.AttemptsIssued);
        Assert.Equal(requested, search.Requested);
        Assert.Equal(PortSearchStopReason.RetryDisabled, search.StopReason);
    }

    [Fact]
    public void DisabledLocalSearchAndSuccessfulAttemptDoNotIssueAnotherCandidate()
    {
        PortSearch disabled = new PortSearchPolicy().Begin(DomainData.Rule(auto: false));
        disabled.Next();
        Assert.Null(disabled.Next(TunnelFailureKind.LocalAddressInUse));
        Assert.Equal(PortSearchStopReason.RetryDisabled, disabled.StopReason);
        PortSearch success = new PortSearchPolicy().Begin(DomainData.Rule());
        success.Next();
        Assert.Null(success.Next());
        Assert.Equal(PortSearchStopReason.Completed, success.StopReason);
    }

    [Fact]
    public void TotalBudgetClampsEachAttemptAndExpiresWithoutWallClockSleep()
    {
        var clock = new ManualTimeProvider();
        PortSearch search = new PortSearchPolicy(clock, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2)).Begin(DomainData.Rule());
        Assert.Equal(TimeSpan.FromSeconds(2), search.Next()!.Timeout);
        clock.Advance(TimeSpan.FromSeconds(4.5));
        Assert.Equal(TimeSpan.FromMilliseconds(500), search.Next(TunnelFailureKind.LocalAddressInUse)!.Timeout);
        clock.Advance(TimeSpan.FromMilliseconds(500));
        Assert.Null(search.Next(TunnelFailureKind.LocalAddressInUse));
        Assert.Equal(PortSearchStopReason.DeadlineExceeded, search.StopReason);
        Assert.Equal(2, search.AttemptsIssued);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancellationStopsBeforeIssuingTheNextAttempt(bool afterInitial)
    {
        using var cancellation = new CancellationTokenSource();
        PortSearch search = new PortSearchPolicy().Begin(DomainData.Rule(), cancellation.Token);
        if (afterInitial) { search.Next(); }
        cancellation.Cancel();
        Assert.Null(afterInitial ? search.Next(TunnelFailureKind.LocalAddressInUse) : search.Next());
        Assert.Equal(afterInitial ? 1 : 0, search.AttemptsIssued);
        Assert.Equal(PortSearchStopReason.Cancelled, search.StopReason);
    }

    [Fact]
    public void ExpiredBudgetAndInvalidConfigurationCannotStart()
    {
        var clock = new ManualTimeProvider();
        var policy = new PortSearchPolicy(clock);
        PortSearch search = policy.Begin(DomainData.Rule());
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Null(search.Next());
        Assert.Equal(0, search.AttemptsIssued);
        Assert.Equal(PortSearchStopReason.DeadlineExceeded, search.StopReason);
        Assert.Throws<DomainValidationException>(() => policy.Begin(DomainData.Rule(localPort: 0)));
        Assert.Throws<DomainValidationException>(() => policy.Begin(DomainData.Rule() with { Mode = (ForwardMode)99 }));
    }
}

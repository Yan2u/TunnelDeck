using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Snapshots;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Tests;

public sealed class SnapshotAndDuplicateTests
{
    [Fact]
    public void CrossServerLaunchSnapshotSurvivesConfigurationEditsAndDeletion()
    {
        RemoteServer first = DomainData.Server(1, "Same name");
        RemoteServer second = DomainData.Server(2, "Same name");
        ForwardProfile profile = DomainData.Profile(1, DomainData.Rule(), DomainData.Rule(2, 2, ForwardMode.Remote));
        var clock = new ManualTimeProvider();
        ProfileLaunchSnapshot snapshot = ProfileSnapshotFactory.Capture(profile, [first, second], clock);
        profile = profile with { Name = "Changed", Rules = profile.Rules.SetItem(0, profile.Rules[0] with { LocalPort = 9000 }) };
        first = first with { Host = "replacement.example" };
        Assert.Equal(DomainData.Now, snapshot.CapturedAt);
        Assert.Equal(3000, snapshot.Rules[0].Requested.LocalPort);
        Assert.Equal("server-1.example", snapshot.Rules[0].Server.Host);
        Assert.Equal("Profile 1", snapshot.Rules[0].Source.ProfileName);
        Assert.NotEqual(snapshot.Rules[0].Server.Id, snapshot.Rules[1].Server.Id);
        var history = new ActivityEntry
        {
            Id = DomainData.Id(900), OccurredAt = DomainData.Now, Outcome = ActivityOutcome.Started,
            Requested = snapshot.Rules[0].Requested, Actual = snapshot.Rules[0].Requested.GetRoute(), Source = snapshot.Rules[0].Source,
        };
        AppState afterDeletion = new() { RecentActivity = [history] };
        Assert.True(AppStateValidator.Validate(afterDeletion).IsValid);
        Assert.Equal("Profile 1", afterDeletion.RecentActivity[0].Source.ProfileName);
    }

    [Fact]
    public void SnapshotCannotResolveADeletedServerByDuplicateDisplayName()
    {
        Assert.Throws<DomainValidationException>(() => ProfileSnapshotFactory.Capture(
            DomainData.Profile(1, DomainData.Rule(server: 1)), [DomainData.Server(2, "same")]));
    }

    [Theory]
    [InlineData(TunnelState.Starting)]
    [InlineData(TunnelState.Running)]
    [InlineData(TunnelState.Reconnecting)]
    public void EquivalentRequestedRoutesReuseLiveWorkAcrossProfiles(TunnelState state)
    {
        ForwardRule first = DomainData.Rule() with { RemoteHost = "DB.EXAMPLE." };
        var tunnel = new ActiveTunnel
        {
            Id = DomainData.Id(500), Requested = first, Actual = first.GetRoute() with { Local = new("127.0.0.1", 3001) },
            State = state, CreatedAt = DomainData.Now, Source = new() { ProfileId = DomainData.Id(20), ProfileName = "Original source", ServerName = "Server" },
        };
        ForwardRule repeated = first with { Id = DomainData.Id(600), RemoteHost = "db.example", AutoFindAvailablePort = false };
        DuplicateTunnelDecision decision = DuplicateTunnelPolicy.Evaluate(repeated, [tunnel]);
        Assert.Equal(DuplicateTunnelAction.Reuse, decision.Action);
        Assert.Same(tunnel, decision.Existing);
        Assert.NotNull(decision.Existing);
        Assert.Equal("Original source", decision.Existing.Source.ProfileName);
        Assert.Equal(DuplicateTunnelAction.Create, DuplicateTunnelPolicy.Evaluate(repeated with { LocalPort = 3001 }, [tunnel]).Action);
        Assert.Equal(DuplicateTunnelAction.Create, DuplicateTunnelPolicy.Evaluate(repeated with { ServerId = DomainData.Id(2) }, [tunnel]).Action);
        Assert.Equal(DuplicateTunnelAction.Create, DuplicateTunnelPolicy.Evaluate(repeated with { Mode = ForwardMode.Remote }, [tunnel]).Action);
        Assert.Equal(DuplicateTunnelAction.Create, DuplicateTunnelPolicy.Evaluate(repeated with { RemotePort = 4000 }, [tunnel]).Action);
    }

    [Theory]
    [InlineData(TunnelState.Stopped)]
    [InlineData(TunnelState.Failed)]
    [InlineData(TunnelState.Cancelled)]
    public void TerminalWorkDoesNotBlockANewRequest(TunnelState state)
    {
        ForwardRule rule = DomainData.Rule();
        Assert.Equal(DuplicateTunnelAction.Create, DuplicateTunnelPolicy.Evaluate(rule, [new() { Requested = rule, State = state }]).Action);
    }

    [Fact]
    public void StoppingOrStopRequestedWorkMustFinishBeforeAnotherStart()
    {
        ForwardRule rule = DomainData.Rule();
        Assert.Equal(DuplicateTunnelAction.WaitForStop, DuplicateTunnelPolicy.Evaluate(rule, [new() { Requested = rule, State = TunnelState.Stopping }]).Action);
        Assert.Equal(DuplicateTunnelAction.WaitForStop, DuplicateTunnelPolicy.Evaluate(rule, [new() { Requested = rule, State = TunnelState.Running, StopRequested = true }]).Action);
    }

    [Fact]
    public void IpNormalizationDoesNotResolveDnsOrTreatDifferentTargetsAsDuplicates()
    {
        ForwardRule rule = DomainData.Rule() with { LocalHost = "::1" };
        Assert.Equal(ForwardRequestKey.FromRule(rule), ForwardRequestKey.FromRule(rule with { LocalHost = "0:0:0:0:0:0:0:1" }));
        Assert.NotEqual(ForwardRequestKey.FromRule(rule), ForwardRequestKey.FromRule(rule with { LocalHost = "localhost" }));
    }

    [Fact]
    public void StopAndGenerationGuardsRejectLateCompletions()
    {
        var tunnel = new ActiveTunnel { Generation = 2, State = TunnelState.Starting };
        Assert.True(TunnelStatePolicy.CanApplyStartCompletion(tunnel, 2));
        Assert.False(TunnelStatePolicy.CanApplyStartCompletion(tunnel, 1));
        Assert.False(TunnelStatePolicy.CanApplyStartCompletion(tunnel with { StopRequested = true }, 2));
        Assert.False(TunnelStatePolicy.CanApplyStartCompletion(tunnel with { State = TunnelState.Stopped }, 2));
        Assert.True(TunnelStatePolicy.CanTransition(TunnelState.Starting, TunnelState.Stopping, stopRequested: true));
        Assert.False(TunnelStatePolicy.CanTransition(TunnelState.Reconnecting, TunnelState.Running, stopRequested: true));
        Assert.False(TunnelStatePolicy.CanTransition(TunnelState.Stopped, TunnelState.Running));
        Assert.False((tunnel with { State = TunnelState.Reconnecting }).IsRunning);
        Assert.True((tunnel with { State = TunnelState.Running }).IsRunning);
    }
}

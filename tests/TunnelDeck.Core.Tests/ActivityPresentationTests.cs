using System.Collections.Immutable;
using System.Globalization;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class ActivityPresentationTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(0, "Just now")]
    [InlineData(59, "Just now")]
    [InlineData(60, "1 min ago")]
    [InlineData(3599, "59 min ago")]
    [InlineData(3600, "1 hr ago")]
    public void RelativeTimesUseTheInjectedClockAtMinuteAndHourBoundaries(int elapsed, string expected)
    {
        var clock = new TimerTimeProvider();
        DateTimeOffset occurredAt = clock.GetUtcNow();
        clock.Advance(TimeSpan.FromSeconds(elapsed));
        Assert.Equal(expected, ActivityTimeFormatter.Format(occurredAt, clock.GetUtcNow(), TimeZoneInfo.Utc, Culture).Relative);
    }

    [Fact]
    public void YesterdayUsesTheLocalCalendarAndExactTimesIncludeTheUtcOffset()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test +08", TimeSpan.FromHours(8), "Test +08", "Test +08");
        DateTimeOffset now = new(2026, 9, 21, 16, 30, 0, TimeSpan.Zero);
        ActivityTimeText result = ActivityTimeFormatter.Format(now.AddHours(-2), now, zone, Culture);
        Assert.Equal("Yesterday, 22:30", result.Relative);
        Assert.Equal("09/21/2026 22:30 (UTC+08:00)", result.Exact);
        Assert.DoesNotContain("ago", ActivityTimeFormatter.Format(now.AddMinutes(1), now, zone, Culture).Relative);
        Assert.Contains("2025", ActivityTimeFormatter.Format(now.AddYears(-1), now, zone, Culture).Relative);
    }

    [Fact]
    public void RepeatedLocalTimesAtTheDstTransitionKeepDifferentOffsetsAndElapsedTimes()
    {
        TimeZoneInfo zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        DateTimeOffset first = new(2026, 11, 1, 5, 30, 0, TimeSpan.Zero);
        DateTimeOffset second = first.AddHours(1);
        ActivityTimeText before = ActivityTimeFormatter.Format(first, second, zone, Culture);
        ActivityTimeText after = ActivityTimeFormatter.Format(second, second, zone, Culture);
        Assert.Equal("1 hr ago", before.Relative);
        Assert.Contains("01:30 (UTC-04:00)", before.Exact);
        Assert.Contains("01:30 (UTC-05:00)", after.Exact);
    }

    [Fact]
    public void HistoryKeepsNamesEndpointsAndErrorsAfterResourcesAreRenamedOrDeleted()
    {
        ActivityEntry entry = Entry() with
        {
            Outcome = ActivityOutcome.Failed,
            Actual = new(new("127.0.0.1", 3002), new("::1", 5432)),
            Source = new() { ServerName = "Original server", ProfileId = DomainData.Id(10), ProfileName = "Deleted profile" },
            Error = new(TunnelFailureKind.Connectivity, "The connection was lost."),
        };
        AppState state = DomainData.State([DomainData.Server(1, "Renamed server")], []) with { RecentActivity = [entry] };
        var item = new RecentActivityItem(entry, state);
        item.UpdateTime(DomainData.Now, TimeZoneInfo.Utc, Culture);
        Assert.Equal("Deleted profile", item.Name);
        Assert.Contains("Original server", item.Route);
        Assert.Contains("Failed", item.Route);
        Assert.Contains(":3002", item.Route);
        Assert.Contains("3000", item.Details);
        Assert.Contains("[::1]:5432", item.Details);
        Assert.Contains("Server renamed", item.SourceNote);
        Assert.Contains("Profile deleted", item.SourceNote);
        Assert.Equal(entry.Error.Message, item.Error);
        Assert.Equal(entry, item.Entry);
    }

    [Fact]
    public void RemoteRoutesFollowTheActualDirectionAndDuplicateHistoryNamesRemainDistinct()
    {
        ActivityEntry first = Entry() with { Requested = DomainData.Rule(mode: ForwardMode.Remote, auto: false) };
        ActivityEntry other = first with { Id = DomainData.Id(901), Requested = first.Requested with { ServerId = DomainData.Id(2) } };
        AppState state = new() { RecentActivity = [first, other] };
        var one = new RecentActivityItem(first, state);
        var two = new RecentActivityItem(other, state);
        Assert.NotEqual(one.Name, two.Name);
        Assert.Contains("Remote :5432 → Local :3000", one.Route);
        Assert.Contains("Server deleted", one.SourceNote);
    }

    [Fact]
    public void RankingsExcludeDeletedResourcesAndZeroCountsWhileTotalsStayCumulative()
    {
        AppState state = DomainData.State([DomainData.Server(1, "Same"), DomainData.Server(2, "Same")],
            [DomainData.Profile(1), DomainData.Profile(2)]) with
        {
            Statistics = new()
            {
                TotalForwards = 1008,
                ServerForwards = ImmutableDictionary<Guid, long>.Empty.Add(DomainData.Id(99), 1000).Add(DomainData.Id(1), 4).Add(DomainData.Id(2), 4),
                ProfileUses = ImmutableDictionary<Guid, long>.Empty.Add(DomainData.Id(99), 500).Add(DomainData.Profile(1).Id, 0),
            },
        };
        UsageStatisticsProjection result = UsageStatisticsProjection.Create(state, Culture);
        Assert.Equal("1,008", result.TotalForwards);
        Assert.Equal(DomainData.Id(1), result.Server.Id);
        Assert.Contains(DomainData.Id(1).ToString("N")[..8], result.Server.Name);
        Assert.Contains("4 forwards", result.Server.Detail);
        Assert.Null(result.Profile.Id);
        Assert.Equal(UsageLeader.Empty, UsageStatisticsProjection.Create(state with { Servers = [] }, Culture).Server);
    }

    [Fact]
    public void RenamedProfilesShowCurrentNamesAndDuplicateNamesHaveStableIds()
    {
        var first = DomainData.Profile(1) with { Name = "Same profile" };
        var second = DomainData.Profile(2) with { Name = "Same profile" };
        AppState state = DomainData.State([], [second, first]) with
        {
            Statistics = new() { ProfileUses = ImmutableDictionary<Guid, long>.Empty.Add(first.Id, 3).Add(second.Id, 3) },
        };
        UsageLeader leader = UsageStatisticsProjection.Create(state, Culture).Profile;
        Assert.Equal(first.Id, leader.Id);
        Assert.Contains(first.Id.ToString("N")[..8], leader.Name);
        var renamed = state with { Profiles = [first with { Name = "Renamed" }, second] };
        Assert.Equal("Renamed", UsageStatisticsProjection.Create(renamed, Culture).Profile.Name);
        Assert.Equal("3 uses", leader.Detail);
    }

    private static ActivityEntry Entry() => new()
    {
        Id = DomainData.Id(900), OccurredAt = DomainData.Now, Outcome = ActivityOutcome.Started,
        Requested = DomainData.Rule(), Source = new() { ServerName = "Same server" },
    };
}

using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class TunnelItemTests
{
    [Theory]
    [InlineData(ForwardMode.Local, true)]
    [InlineData(ForwardMode.Remote, false)]
    public void OnlyTheChangedLocalListenerGetsAnAdjustmentMarker(ForwardMode mode, bool adjusted)
    {
        var rule = DomainData.Rule(mode: mode);
        var row = new TunnelItem(new() { Requested = rule, Actual = rule.GetRoute() with { Local = new("127.0.0.1", 3001) } }, new(), DomainData.Now);
        Assert.Equal(adjusted, row.PortAdjusted);
        Assert.Equal(mode == ForwardMode.Local ? "Local :3001" : "Remote :5432", row.LeadingEndpoint);
        Assert.Contains("3000", row.RequestedRoute);
    }

    [Fact]
    public void DurationIsDerivedOnDemandAndFreezesAtTheRecordedEnd()
    {
        var value = new ActiveTunnel { StartedAt = DomainData.Now.AddMinutes(-1), State = TunnelState.Running };
        var row = new TunnelItem(value, new(), DomainData.Now);
        Assert.Equal("00:01:00", row.Duration);
        row.RefreshTime(DomainData.Now.AddSeconds(1));
        Assert.Equal("00:01:01", row.Duration);
        row.Update(value with { State = TunnelState.Failed, EndedAt = DomainData.Now.AddSeconds(1) }, new());
        row.RefreshTime(DomainData.Now.AddHours(1));
        Assert.Equal("00:01:01", row.Duration);
        Assert.Equal("—", TunnelItem.FormatDuration(null, DomainData.Now));
    }

    [Fact]
    public void DeletedSourcesKeepTheirNamesButLoseNavigation()
    {
        var profile = DomainData.Profile();
        var state = DomainData.State([DomainData.Server()], [profile]);
        var value = new ActiveTunnel { Requested = DomainData.Rule(), Source = new() { ServerName = "Original server", ProfileId = profile.Id, ProfileName = "Original profile" } };
        var row = new TunnelItem(value, state, DomainData.Now);
        Assert.True(row.CanOpenProfile);
        Assert.True(row.CanOpenServer);
        row.Update(value, new());
        Assert.False(row.CanOpenProfile);
        Assert.False(row.CanOpenServer);
        Assert.Equal("Original server", row.ServerName);
        Assert.Equal("Original profile", row.Source);
    }
}

using TunnelDeck.Core.Drafts;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class RuleDefaultsTests
{
    [Theory]
    [InlineData(1, 2, 3, 4, 1)]
    [InlineData(99, 2, 3, 4, 2)]
    [InlineData(0, 99, 3, 4, 3)]
    [InlineData(0, 0, 99, 4, 4)]
    [InlineData(99, 99, 99, 99, 0)]
    [InlineData(0, 0, 0, 0, 0)]
    public void PrioritySkipsDeletedOrAbsentServerIds(int previous, int current, int pinned, int fallback, int expected)
    {
        var available = Enumerable.Range(1, 4).Select(DomainData.Id).ToHashSet();
        var settings = new AppSettings { PinnedQuickServerId = Optional(pinned), DefaultServerId = Optional(fallback) };
        Assert.Equal(Optional(expected), RuleDefaultsResolver.ResolveServerId(available, settings, Optional(previous), Optional(current)));
    }

    [Fact]
    public void EmptyProfileAndRepeatedDisplayNamesStillUseIds()
    {
        RemoteServer[] servers = [DomainData.Server(1, "same"), DomainData.Server(2, "same")];
        var settings = new AppSettings { DefaultServerId = servers[0].Id };
        ForwardRuleDraft draft = RuleDefaultsResolver.CreateRuleDraft(servers.Select(server => server.Id).ToHashSet(), settings, DomainData.Profile(), servers[1].Id);
        Assert.Equal(servers[1].Id, draft.ServerId);
        ForwardProfile profile = DomainData.Profile(1, DomainData.Rule(server: 1));
        Assert.Equal(servers[0].Id, RuleDefaultsResolver.CreateRuleDraft(servers.Select(server => server.Id).ToHashSet(), settings, profile, servers[1].Id).ServerId);
    }

    [Fact]
    public void LocalDefaultsAffectOnlyNewDraftsAndRemoteNeverSavesTheFlag()
    {
        var servers = new HashSet<Guid> { DomainData.Id(1) };
        var settings = new AppSettings { DefaultServerId = DomainData.Id(1) };
        ForwardRuleDraft existing = RuleDefaultsResolver.CreateRuleDraft(servers, settings);
        settings = settings with { AutoFindAvailablePortDefault = false };
        Assert.True(existing.AutoFindAvailablePort);
        Assert.False(RuleDefaultsResolver.CreateRuleDraft(servers, settings).AutoFindAvailablePort);
        Assert.Equal("127.0.0.1", existing.LocalHost);
        Assert.Equal("127.0.0.1", existing.RemoteHost);
        existing.LocalPortText = "3000";
        existing.RemotePortText = "3000";
        existing.Mode = ForwardMode.Remote;
        Assert.False(existing.AutoFindAvailablePort);
        Assert.False(existing.Build(servers).AutoFindAvailablePort);
        existing.Mode = ForwardMode.Local;
        Assert.True(existing.AutoFindAvailablePort);
        Assert.False(RuleDefaultsResolver.CreateRuleDraft(servers, new AppSettings(), mode: ForwardMode.Remote).AutoFindAvailablePort);
    }

    [Fact]
    public void EditingAndDiscardingDraftsDoesNotModifySavedObjects()
    {
        RemoteServer server = DomainData.Server(1, "Original");
        ServerDraft serverDraft = ServerDraft.FromServer(server);
        serverDraft.DisplayName = "Edited";
        serverDraft.PortText = "2222";
        Assert.Equal("Original", server.DisplayName);
        Assert.Equal(22, server.Port);
        Assert.Equal(2222, serverDraft.Build().Port);
        ForwardProfile profile = DomainData.Profile(1, DomainData.Rule());
        ProfileDraft profileDraft = ProfileDraft.FromProfile(profile);
        profileDraft.Name = "Edited profile";
        profileDraft.Rules[0].LocalPortText = "9000";
        ForwardProfile savedCandidate = profileDraft.Build();
        profileDraft.Rules.Clear();
        Assert.Equal(3000, profile.Rules[0].LocalPort);
        Assert.Equal(9000, savedCandidate.Rules[0].LocalPort);
        Assert.Single(profile.Rules);
        Assert.Single(savedCandidate.Rules);
    }

    private static Guid? Optional(int id) => id == 0 ? null : DomainData.Id(id);
}

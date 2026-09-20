using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class DeletionConfirmationTests
{
    [Fact]
    public void ZeroSectionsAreAbsentAndSingleServerShowsAnUnambiguousEndpoint()
    {
        var state = DomainData.State([DomainData.Server()], []);
        var impact = DeletionImpactCalculator.Calculate(state, new(0, []), new(0, []), new(ResourceKind.Server, [DomainData.Id(1)]));
        var model = new DeletionConfirmationModel(impact);
        Assert.Empty(model.RuntimeSummary); Assert.Empty(model.PendingSummary); Assert.Empty(model.TestsSummary);
        Assert.Empty(model.RulesSummary); Assert.False(model.ShowDetails);
        Assert.Contains("dev@server-1.example:22", model.ResourceSummary);
        Assert.Equal("Delete", model.ActionLabel);
    }

    [Fact]
    public void BulkConfirmationContainsDeduplicatedCountsWithoutRuleDetails()
    {
        var state = DomainData.State([DomainData.Server(), DomainData.Server(2)],
            [DomainData.Profile(1, DomainData.Rule(1, 1), DomainData.Rule(2, 2))]);
        var impact = DeletionImpactCalculator.Calculate(state, new(0, []), new(0, []), new(ResourceKind.Server, [], true));
        var model = new DeletionConfirmationModel(impact);
        Assert.Equal("2 forwarding rules in 1 profile will be removed.", model.RulesSummary);
        Assert.Empty(model.Groups); Assert.False(model.ShowDetails); Assert.Equal("Delete all", model.ActionLabel);
    }

    [Fact]
    public void LargeSingleServerDetailsAreInitiallyCollapsedAndProfilesAreKept()
    {
        var state = DomainData.State([DomainData.Server()],
            [DomainData.Profile(1, Enumerable.Range(1, 30).Select(id => DomainData.Rule(id)).ToArray())]);
        var impact = DeletionImpactCalculator.Calculate(state, new(0, []), new(0, []), new(ResourceKind.Server, [DomainData.Id(1)]));
        var model = new DeletionConfirmationModel(impact, "Review changed rules.");
        Assert.True(model.ShowDetails); Assert.False(model.ExpandDetails); Assert.Single(model.Groups);
        Assert.Equal(30, model.Groups[0].Details.Split('\n').Length);
        Assert.Contains("Profiles are kept", model.KeptProfiles); Assert.Equal("Review changed rules.", model.UpdateNotice);
    }
}

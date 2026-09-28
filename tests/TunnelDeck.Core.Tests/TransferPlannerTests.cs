using TunnelDeck.Core.Models;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Transfer;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Tests;

public sealed class TransferPlannerTests
{
    internal static AppState Source()
    {
        AppState state = DomainData.State(
            [DomainData.Server(1, "Same name", DomainData.Now) with { CredentialReference = DomainData.Id(900) }, DomainData.Server(2, "Same name")],
            [DomainData.Profile(1, DomainData.Rule(), DomainData.Rule(2, 2, ForwardMode.Remote)), DomainData.Profile(2)]);
        return state with
        {
            Order = state.Order with { PinnedServerIds = [state.Servers[1].Id], OtherServerIds = [state.Servers[0].Id],
                PinnedProfileIds = [state.Profiles[0].Id], OtherProfileIds = [state.Profiles[1].Id] },
        };
    }

    [Fact]
    public void ProfilesExportIncludesAllDependenciesAndTheirOriginalOrder()
    {
        AppState source = Source();
        TunnelDeckExport export = ExportBuilder.Build(source, ExportScope.Profiles, DomainData.Now, [source.Profiles[0].Id]);
        Assert.Single(export.Profiles);
        Assert.Equal(2, export.Servers.Length);
        Assert.Equal(source.Order.PinnedServerIds.ToArray(), export.Order.PinnedServerIds.ToArray());
        Assert.Empty(export.Order.OtherProfileIds);
        Assert.All(export.Profiles.SelectMany(profile => profile.Rules), rule => Assert.Contains(export.Servers, server => server.Id == rule.ServerId));
    }

    [Fact]
    public void PlanMapsEveryIdAndKeepsNamesEndpointsRulesAndPinnedOrder()
    {
        AppState source = Source();
        ImportPlan plan = ImportPlanner.Plan(ExportBuilder.Build(source, ExportScope.Everything, DomainData.Now), source, _ => true);
        Assert.Equal(4, plan.UsableCount);
        Assert.True(plan.HasWarnings);
        Assert.All(plan.Servers, server =>
        {
            Assert.DoesNotContain(source.Servers, old => old.Id == server.Id);
            Assert.Null(server.CredentialReference); Assert.Null(server.LastUsedAt);
        });
        for (int index = 0; index < source.Profiles[0].Rules.Length; index++)
        {
            ForwardRule before = source.Profiles[0].Rules[index], after = plan.Profiles[0].Rules[index];
            Assert.NotEqual(before.Id, after.Id);
            Assert.Equal(before.Mode, after.Mode); Assert.Equal(before.LocalPort, after.LocalPort); Assert.Equal(before.RemotePort, after.RemotePort);
            Assert.Equal(source.Servers.Single(server => server.Id == before.ServerId).Host, plan.Servers.Single(server => server.Id == after.ServerId).Host);
        }
        Assert.Equal(plan.Servers[1].Id, Assert.Single(plan.Order.PinnedServerIds));
        Assert.Equal(plan.Profiles[0].Id, Assert.Single(plan.Order.PinnedProfileIds));
        Assert.Empty(plan.Profiles[1].Rules);
        AppStateValidator.Validate(new() { Servers = plan.Servers, Profiles = plan.Profiles, Order = plan.Order }).ThrowIfInvalid();
        Assert.NotNull(source.Servers[0].CredentialReference);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void UnsupportedSchemaNeverMakesAPartialPlan(int version) =>
        Assert.Throws<ImportDocumentException>(() => ImportPlanner.Plan(Export() with { SchemaVersion = version }, new(), _ => true));

    [Theory]
    [InlineData("server-id")]
    [InlineData("empty-id")]
    [InlineData("profile-id")]
    [InlineData("rule-id")]
    [InlineData("port")]
    [InlineData("reference")]
    [InlineData("order")]
    [InlineData("mode")]
    [InlineData("remote-search")]
    [InlineData("cross-resource-id")]
    public void InvalidEntriesRejectTheEntireDocument(string fault)
    {
        TunnelDeckExport export = Export();
        export = fault switch
        {
            "server-id" => export with { Servers = export.Servers.SetItem(1, export.Servers[1] with { Id = export.Servers[0].Id }) },
            "empty-id" => export with { Servers = export.Servers.SetItem(0, export.Servers[0] with { Id = Guid.Empty }) },
            "profile-id" => export with { Profiles = export.Profiles.SetItem(1, export.Profiles[1] with { Id = export.Profiles[0].Id }) },
            "rule-id" => ChangeRule(export, rule => rule with { Id = export.Profiles[0].Rules[0].Id }),
            "port" => export with { Servers = export.Servers.SetItem(1, export.Servers[1] with { Port = 65536 }) },
            "reference" => ChangeRule(export, rule => rule with { ServerId = Guid.NewGuid() }),
            "order" => export with { Order = export.Order with { OtherServerIds = [] } },
            "mode" => ChangeRule(export, rule => rule with { Mode = (ForwardMode)99 }),
            "remote-search" => ChangeRule(export, rule => rule with { AutoFindAvailablePort = true }),
            "cross-resource-id" => ChangeRule(export, rule => rule with { Id = export.Servers[0].Id }),
            _ => throw new InvalidOperationException(),
        };
        Assert.Throws<ImportDocumentException>(() => ImportPlanner.Plan(export, new(), _ => true));
    }

    [Fact]
    public void MissingPrivateKeyRejectsWholeJsonAndDoesNotFallBackToPassword()
    {
        TunnelDeckExport export = Export();
        export = export with { Servers = export.Servers.SetItem(1, export.Servers[1] with
        { AuthMethod = AuthenticationMethod.PrivateKey, IdentityFilePath = @"C:\missing\private key" }) };
        Assert.Contains("private-key", Assert.Throws<ImportDocumentException>(() => ImportPlanner.Plan(export, new(), _ => false)).Message);
    }

    [Fact]
    public void EmptyExportIsValidButNotCommittable() =>
        Assert.False(ImportPlanner.Plan(new(), new(), _ => true).CanCommit);

    internal static TunnelDeckExport Export() => ExportBuilder.Build(Source(), ExportScope.Everything, DomainData.Now);
    private static TunnelDeckExport ChangeRule(TunnelDeckExport export, Func<ExportedRule, ExportedRule> change) => export with
    { Profiles = export.Profiles.SetItem(0, export.Profiles[0] with { Rules = export.Profiles[0].Rules.SetItem(1, change(export.Profiles[0].Rules[1])) }) };
}

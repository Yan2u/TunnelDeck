using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Snapshots;
using TunnelDeck.Core.Tests.Fixtures;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Tests;

public sealed class AppStateContractTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void CompleteStateRoundTripsAllResourceOrdersSettingsHistoryAndCounts()
    {
        AppState state = PopulatedState();
        Assert.True(AppStateValidator.Validate(state).IsValid);
        string json = JsonSerializer.Serialize(state, JsonOptions);
        AppState restored = Assert.IsType<AppState>(JsonSerializer.Deserialize<AppState>(json, JsonOptions));
        Assert.True(AppStateValidator.Validate(restored).IsValid);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(JsonSerializer.Serialize(restored, JsonOptions))));
        Assert.Equal(state.Order.PinnedProfileIds.ToArray(), restored.Order.PinnedProfileIds.ToArray());
        Assert.Equal(state.Order.OtherProfileIds.ToArray(), restored.Order.OtherProfileIds.ToArray());
        Assert.Equal(state.Order.PinnedServerIds.ToArray(), restored.Order.PinnedServerIds.ToArray());
        Assert.Equal(state.Order.OtherServerIds.ToArray(), restored.Order.OtherServerIds.ToArray());
        Assert.Equal(state.Servers[0].CredentialReference, restored.Servers[0].CredentialReference);
        Assert.Equal(41, restored.Statistics.TotalForwards);
        Assert.Equal(41, restored.Statistics.ServerForwards[DomainData.Id(1)]);
    }

    [Fact]
    public void PersistenceRootExcludesRuntimeTrustAndSecretMaterial()
    {
        string secret = "fixture-secret-" + Guid.NewGuid();
        var credentials = new ConnectionCredentials(secret);
        Assert.DoesNotContain(secret, credentials.ToString());
        Assert.Equal("{}", JsonSerializer.Serialize(credentials, JsonOptions));
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(PopulatedState(), JsonOptions));
        JsonElement root = document.RootElement;
        Assert.False(root.TryGetProperty("activeTunnels", out _));
        Assert.False(root.TryGetProperty("trustedHostKeys", out _));
        Assert.DoesNotContain(typeof(AppState).GetProperties(), property => property.PropertyType == typeof(ActiveTunnel));
        Assert.DoesNotContain(typeof(AppState).Assembly.GetReferencedAssemblies(), assembly =>
            assembly.Name!.StartsWith("Renci.", StringComparison.Ordinal) || assembly.Name.StartsWith("Microsoft.UI", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void UnsupportedSchemaIsReportedWithoutResetOrMutation(int version)
    {
        AppState state = PopulatedState() with { SchemaVersion = version };
        string before = JsonSerializer.Serialize(state, JsonOptions);
        ValidationResult result = AppStateValidator.Validate(state);
        Assert.Contains(result.Issues, issue => issue.Path == "SchemaVersion");
        Assert.Equal(before, JsonSerializer.Serialize(state, JsonOptions));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":1}")]
    public void MissingPersistenceEnvelopeDoesNotDeserializeIntoAnEmptyReplacement(string json) =>
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AppState>(json, JsonOptions));

    [Fact]
    public void MissingReferencesDuplicateIdsAndInvalidOrdersAreReported()
    {
        AppState valid = PopulatedState();
        AppState broken = valid with
        {
            Servers = [valid.Servers[0], valid.Servers[0]],
            Order = valid.Order with { OtherServerIds = valid.Order.PinnedServerIds },
            Profiles = valid.Profiles.SetItem(0, valid.Profiles[0] with
            {
                Rules = valid.Profiles[0].Rules.Add(DomainData.Rule(88, server: 9)),
            }),
        };
        ValidationResult result = AppStateValidator.Validate(broken);
        Assert.Contains(result.Issues, issue => issue.Path == "Servers[1].Id");
        Assert.Contains(result.Issues, issue => issue.Path == "Order.Servers");
        Assert.Contains(result.Issues, issue => issue.Path.EndsWith("ServerId", StringComparison.Ordinal));
        Assert.Contains(result.Issues, issue => issue.Path == "Settings.PinnedQuickServerId");
    }

    [Fact]
    public void UnknownEnumsAndRemoteAutoFlagAreRejectedInStoredConfiguration()
    {
        AppState state = PopulatedState();
        ForwardRule invalid = state.Profiles[0].Rules[0] with { Mode = (ForwardMode)900 };
        state = state with
        {
            Servers = state.Servers.SetItem(0, state.Servers[0] with { AuthMethod = (AuthenticationMethod)900 }),
            Profiles = state.Profiles.SetItem(0, state.Profiles[0] with { Rules = [invalid] }),
        };
        ValidationResult result = AppStateValidator.Validate(state);
        Assert.Contains(result.Issues, issue => issue.Path == "Servers[0].AuthMethod");
        Assert.Contains(result.Issues, issue => issue.Path == "Profiles[0].Rules[0].Mode");
        Assert.False(ConfigurationValidator.ValidateRule(DomainData.Rule(mode: ForwardMode.Remote) with { AutoFindAvailablePort = true }).IsValid);
    }

    [Fact]
    public void DuplicateRuleIdsAcrossProfilesAreInvalid()
    {
        AppState state = DomainData.State([DomainData.Server()], [DomainData.Profile(1, DomainData.Rule()), DomainData.Profile(2, DomainData.Rule())]);
        Assert.Contains(AppStateValidator.Validate(state).Issues, issue => issue.Path == "Profiles[1].Rules");
    }

    [Fact]
    public void NullAndUninitializedCollectionsProduceDiagnosticsInsteadOfCrashing()
    {
        AppState state = new() { Servers = default, Profiles = [null!], Order = null!, Settings = null!, Statistics = null!, RecentActivity = default };
        ValidationResult result = AppStateValidator.Validate(state);
        Assert.Contains(result.Issues, issue => issue.Path == "Servers");
        Assert.Contains(result.Issues, issue => issue.Path == "Profiles[0]");
        Assert.Contains(result.Issues, issue => issue.Path == "Order");
        Assert.Contains(result.Issues, issue => issue.Path == "Settings");
        Assert.Contains(result.Issues, issue => issue.Path == "Statistics");
        Assert.Contains(result.Issues, issue => issue.Path == "RecentActivity");
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(5, true)]
    [InlineData(10, true)]
    [InlineData(2, false)]
    [InlineData(11, false)]
    public void SidebarSettingsRespectTheSupportedRange(int maximum, bool valid) =>
        Assert.Equal(valid, ConfigurationValidator.ValidateSettings(new AppSettings { SidebarMaximumItems = maximum }).IsValid);

    [Fact]
    public void NonUtcTimesNegativeCountsAndNegativeRevisionsAreInvalid()
    {
        AppState state = PopulatedState();
        state = state with
        {
            Revision = -1,
            Servers = state.Servers.SetItem(0, state.Servers[0] with { LastUsedAt = DomainData.Now.ToOffset(TimeSpan.FromHours(8)) }),
            Statistics = new() { TotalForwards = -1, ProfileUses = ImmutableDictionary<Guid, long>.Empty.Add(DomainData.Id(1), -1) },
        };
        ValidationResult result = AppStateValidator.Validate(state);
        Assert.Contains(result.Issues, issue => issue.Path == "Revision");
        Assert.Contains(result.Issues, issue => issue.Path == "Servers[0].LastUsedAt");
        Assert.Contains(result.Issues, issue => issue.Path == "Statistics.TotalForwards");
        Assert.Contains(result.Issues, issue => issue.Path == "Statistics.ProfileUses");
    }

    [Fact]
    public void TrimmingRecentActivityDoesNotChangeCumulativeCounts()
    {
        AppState state = PopulatedState();
        AppState trimmed = state with { RecentActivity = [] };
        Assert.Same(state.Statistics, trimmed.Statistics);
        Assert.Equal(41, trimmed.Statistics.TotalForwards);
        Assert.True(AppStateValidator.Validate(trimmed).IsValid);
    }

    [Fact]
    public void EndpointFormattingAndDnsValidationHaveUnambiguousBoundaries()
    {
        Assert.Equal("[::1]:22", new NetworkEndpoint("::1", 22).ToString());
        Assert.Equal("[::1]:22", new NetworkEndpoint("[::1]", 22).ToString());
        Assert.True(HostAddress.IsValidHost("db.example."));
        Assert.False(HostAddress.IsValidHost("db.example.."));
    }

    private static AppState PopulatedState()
    {
        RemoteServer first = DomainData.Server(1, "Same", DomainData.Now) with { CredentialReference = DomainData.Id(9000) };
        RemoteServer second = DomainData.Server(2, "Same", DomainData.Now) with
        {
            AuthMethod = AuthenticationMethod.PrivateKey, IdentityFilePath = "C:\\keys\\example",
        };
        ForwardProfile firstProfile = DomainData.Profile(1, DomainData.Rule(), DomainData.Rule(2, 2, ForwardMode.Remote));
        ForwardProfile secondProfile = DomainData.Profile(2);
        ProfileLaunchSnapshot snapshot = ProfileSnapshotFactory.Capture(firstProfile, [first, second], new ManualTimeProvider());
        return new AppState
        {
            Revision = 42, Servers = [first, second], Profiles = [firstProfile, secondProfile],
            Settings = new() { DefaultServerId = first.Id, PinnedQuickServerId = second.Id, SidebarMaximumItems = 10, AutoFindAvailablePortDefault = false },
            Order = new() { PinnedServerIds = [first.Id], OtherServerIds = [second.Id], PinnedProfileIds = [firstProfile.Id], OtherProfileIds = [secondProfile.Id] },
            Statistics = new()
            {
                TotalForwards = 41,
                ServerForwards = ImmutableDictionary<Guid, long>.Empty.Add(first.Id, 41),
                ProfileUses = ImmutableDictionary<Guid, long>.Empty.Add(firstProfile.Id, 10),
            },
            RecentActivity =
            [
                new ActivityEntry
                {
                    Id = DomainData.Id(6000), OccurredAt = DomainData.Now, Outcome = ActivityOutcome.Started,
                    Requested = snapshot.Rules[0].Requested, Actual = snapshot.Rules[0].Requested.GetRoute() with { Local = new("127.0.0.1", 3001) },
                    Source = snapshot.Rules[0].Source,
                },
            ],
        };
    }
}

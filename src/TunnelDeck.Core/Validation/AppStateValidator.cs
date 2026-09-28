using System.Collections.Immutable;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Validation;

/// <summary>Reports invalid persisted input without repairing, mutating or resetting user data.</summary>
public static class AppStateValidator
{
    public static ValidationResult Validate(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var result = new ValidationBuilder();
        result.Require(state.SchemaVersion == AppState.CurrentSchemaVersion, nameof(state.SchemaVersion), UiText.Invariant("Diagnostic_AppStateValidator_ThisSchemaVersionIsNotSupportedDoNot"));
        result.Require(state.Revision >= 0, nameof(state.Revision), UiText.Invariant("Diagnostic_AppStateValidator_RevisionCannotBeNegative"));
        var servers = new HashSet<Guid>();
        var profiles = new HashSet<Guid>();
        var rules = new HashSet<Guid>();
        result.Require(!state.Servers.IsDefault, nameof(state.Servers), UiText.Invariant("Diagnostic_AppStateValidator_TheServersCollectionIsRequired"));
        result.Require(!state.Profiles.IsDefault, nameof(state.Profiles), UiText.Invariant("Diagnostic_AppStateValidator_TheProfilesCollectionIsRequired"));

        if (!state.Servers.IsDefault)
        {
            for (int index = 0; index < state.Servers.Length; index++)
            {
                RemoteServer? server = state.Servers[index];
                if (server is null) { result.Require(false, $"Servers[{index}]", UiText.Invariant("Diagnostic_AppStateValidator_AServerCannotBeNull")); continue; }
                result.Require(servers.Add(server.Id), $"Servers[{index}].Id", UiText.Invariant("Diagnostic_AppStateValidator_ServerIDsMustBeUnique"));
                result.Add($"Servers[{index}]", ConfigurationValidator.ValidateServer(server));
            }
        }
        if (!state.Profiles.IsDefault)
        {
            for (int index = 0; index < state.Profiles.Length; index++)
            {
                ForwardProfile? profile = state.Profiles[index];
                if (profile is null) { result.Require(false, $"Profiles[{index}]", UiText.Invariant("Diagnostic_AppStateValidator_AProfileCannotBeNull")); continue; }
                result.Require(profiles.Add(profile.Id), $"Profiles[{index}].Id", UiText.Invariant("Diagnostic_AppStateValidator_ProfileIDsMustBeUnique"));
                result.Add($"Profiles[{index}]", ConfigurationValidator.ValidateProfile(profile, servers));
                if (profile.Rules.IsDefault) { continue; }
                foreach (ForwardRule? rule in profile.Rules)
                {
                    if (rule is not null) { result.Require(rules.Add(rule.Id), $"Profiles[{index}].Rules", UiText.Invariant("Diagnostic_AppStateValidator_RuleIDsMustBeUniqueAcrossProfiles")); }
                }
            }
        }

        if (state.Settings is null) { result.Require(false, nameof(state.Settings), UiText.Invariant("Diagnostic_AppStateValidator_SettingsAreRequired")); }
        else { result.Add(nameof(state.Settings), ConfigurationValidator.ValidateSettings(state.Settings, servers)); }
        if (state.Order is null) { result.Require(false, nameof(state.Order), UiText.Invariant("Diagnostic_AppStateValidator_ResourceOrderIsRequired")); }
        else
        {
            ValidateOrder(state.Order.PinnedServerIds, state.Order.OtherServerIds, servers, "Order.Servers", result);
            ValidateOrder(state.Order.PinnedProfileIds, state.Order.OtherProfileIds, profiles, "Order.Profiles", result);
        }
        ValidateStatistics(state.Statistics, result);
        ValidateActivity(state.RecentActivity, result);
        return result.Build();
    }

    private static void ValidateOrder(ImmutableArray<Guid> pinned, ImmutableArray<Guid> other, HashSet<Guid> available,
        string path, ValidationBuilder result)
    {
        if (pinned.IsDefault || other.IsDefault) { result.Require(false, path, UiText.Invariant("Diagnostic_AppStateValidator_BothOrderCollectionsAreRequired")); return; }
        Guid[] all = [.. pinned, .. other];
        result.Require(all.Length == all.Distinct().Count(), path, UiText.Invariant("Diagnostic_AppStateValidator_AnIDMayOccurOnlyOnceAcrossPinned"));
        result.Require(available.SetEquals(all), path, UiText.Invariant("Diagnostic_AppStateValidator_OrderMustContainEveryExistingResourceExactlyOnce"));
    }

    private static void ValidateStatistics(UsageStatistics? statistics, ValidationBuilder result)
    {
        if (statistics is null) { result.Require(false, "Statistics", UiText.Invariant("Diagnostic_AppStateValidator_StatisticsAreRequired")); return; }
        result.Require(statistics.TotalForwards >= 0, "Statistics.TotalForwards", UiText.Invariant("Diagnostic_AppStateValidator_CountsCannotBeNegative"));
        ValidateCounts(statistics.ServerForwards, "Statistics.ServerForwards");
        ValidateCounts(statistics.ProfileUses, "Statistics.ProfileUses");

        void ValidateCounts(ImmutableDictionary<Guid, long>? counts, string path)
        {
            if (counts is null) { result.Require(false, path, UiText.Invariant("Diagnostic_AppStateValidator_TheCountsCollectionIsRequired")); return; }
            foreach ((Guid id, long count) in counts)
            {
                result.Id(id, path);
                result.Require(count >= 0, path, UiText.Invariant("Diagnostic_AppStateValidator_CountsCannotBeNegative"));
            }
        }
    }

    private static void ValidateActivity(ImmutableArray<ActivityEntry> entries, ValidationBuilder result)
    {
        if (entries.IsDefault) { result.Require(false, "RecentActivity", UiText.Invariant("Diagnostic_AppStateValidator_TheRecentActivityCollectionIsRequired")); return; }
        result.Require(entries.Length <= AppState.MaximumRecentEntries, "RecentActivity", UiText.Invariant("Diagnostic_AppStateValidator_KeepAtMost200RecentEntriesCumulativeStatistics"));
        var ids = new HashSet<Guid>();
        for (int index = 0; index < entries.Length; index++)
        {
            string path = $"RecentActivity[{index}]";
            ActivityEntry? entry = entries[index];
            if (entry is null) { result.Require(false, path, UiText.Invariant("Diagnostic_AppStateValidator_AnActivityEntryCannotBeNull")); continue; }
            result.Id(entry.Id, $"{path}.Id");
            result.Require(ids.Add(entry.Id), $"{path}.Id", UiText.Invariant("Diagnostic_AppStateValidator_ActivityIDsMustBeUnique"));
            result.Utc(entry.OccurredAt, $"{path}.OccurredAt");
            result.Require(Enum.IsDefined(entry.Outcome), $"{path}.Outcome", UiText.Invariant("Diagnostic_AppStateValidator_ChooseASupportedOutcome"));
            if (entry.Requested is null) { result.Require(false, $"{path}.Requested", UiText.Invariant("Diagnostic_AppStateValidator_ARequestedRouteSnapshotIsRequired")); }
            else { result.Add($"{path}.Requested", ConfigurationValidator.ValidateRule(entry.Requested)); }
            result.Require(entry.Source is not null, $"{path}.Source", UiText.Invariant("Diagnostic_AppStateValidator_ASourceSnapshotIsRequired"));
            if (entry.Source is not null)
            {
                result.Require(!string.IsNullOrWhiteSpace(entry.Source.ServerName), $"{path}.Source.ServerName", UiText.Invariant("Diagnostic_AppStateValidator_AServerNameSnapshotIsRequired"));
                if (entry.Source.ProfileId is Guid profileId)
                {
                    result.Id(profileId, $"{path}.Source.ProfileId");
                    result.Require(!string.IsNullOrWhiteSpace(entry.Source.ProfileName), $"{path}.Source.ProfileName", UiText.Invariant("Diagnostic_AppStateValidator_AProfileNameSnapshotIsRequired"));
                }
                if (entry.Source.RuleId is Guid ruleId) { result.Id(ruleId, $"{path}.Source.RuleId"); }
            }
            if (entry.Error is not null)
            {
                result.Require(Enum.IsDefined(entry.Error.Kind), $"{path}.Error.Kind", UiText.Invariant("Diagnostic_AppStateValidator_UnknownErrorCategory"));
                result.Require(!string.IsNullOrWhiteSpace(entry.Error.Message), $"{path}.Error.Message", UiText.Invariant("Diagnostic_AppStateValidator_AnErrorExplanationIsRequired"));
            }
            if (entry.Actual is ForwardRoute route)
            {
                ValidateEndpoint(route.Local, $"{path}.Actual.Local");
                ValidateEndpoint(route.Remote, $"{path}.Actual.Remote");
            }
        }

        void ValidateEndpoint(NetworkEndpoint? endpoint, string path)
        {
            if (endpoint is null) { result.Require(false, path, UiText.Invariant("Diagnostic_AppStateValidator_AnEndpointIsRequired")); return; }
            result.Port(endpoint.Port, $"{path}.Port");
            result.Require(HostAddress.IsValidHost(endpoint.Host), $"{path}.Host", UiText.Invariant("Diagnostic_AppStateValidator_EnterAValidHost"));
        }
    }
}

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
        result.Require(state.SchemaVersion == AppState.CurrentSchemaVersion, nameof(state.SchemaVersion), "This schema version is not supported; do not overwrite the file.");
        result.Require(state.Revision >= 0, nameof(state.Revision), "Revision cannot be negative.");
        var servers = new HashSet<Guid>();
        var profiles = new HashSet<Guid>();
        var rules = new HashSet<Guid>();
        result.Require(!state.Servers.IsDefault, nameof(state.Servers), "The servers collection is required.");
        result.Require(!state.Profiles.IsDefault, nameof(state.Profiles), "The profiles collection is required.");

        if (!state.Servers.IsDefault)
        {
            for (int index = 0; index < state.Servers.Length; index++)
            {
                RemoteServer? server = state.Servers[index];
                if (server is null) { result.Require(false, $"Servers[{index}]", "A server cannot be null."); continue; }
                result.Require(servers.Add(server.Id), $"Servers[{index}].Id", "Server IDs must be unique.");
                result.Add($"Servers[{index}]", ConfigurationValidator.ValidateServer(server));
            }
        }
        if (!state.Profiles.IsDefault)
        {
            for (int index = 0; index < state.Profiles.Length; index++)
            {
                ForwardProfile? profile = state.Profiles[index];
                if (profile is null) { result.Require(false, $"Profiles[{index}]", "A profile cannot be null."); continue; }
                result.Require(profiles.Add(profile.Id), $"Profiles[{index}].Id", "Profile IDs must be unique.");
                result.Add($"Profiles[{index}]", ConfigurationValidator.ValidateProfile(profile, servers));
                if (profile.Rules.IsDefault) { continue; }
                foreach (ForwardRule? rule in profile.Rules)
                {
                    if (rule is not null) { result.Require(rules.Add(rule.Id), $"Profiles[{index}].Rules", "Rule IDs must be unique across profiles."); }
                }
            }
        }

        if (state.Settings is null) { result.Require(false, nameof(state.Settings), "Settings are required."); }
        else { result.Add(nameof(state.Settings), ConfigurationValidator.ValidateSettings(state.Settings, servers)); }
        if (state.Order is null) { result.Require(false, nameof(state.Order), "Resource order is required."); }
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
        if (pinned.IsDefault || other.IsDefault) { result.Require(false, path, "Both order collections are required."); return; }
        Guid[] all = [.. pinned, .. other];
        result.Require(all.Length == all.Distinct().Count(), path, "An ID may occur only once across Pinned and Other.");
        result.Require(available.SetEquals(all), path, "Order must contain every existing resource exactly once and no missing-resource IDs.");
    }

    private static void ValidateStatistics(UsageStatistics? statistics, ValidationBuilder result)
    {
        if (statistics is null) { result.Require(false, "Statistics", "Statistics are required."); return; }
        result.Require(statistics.TotalForwards >= 0, "Statistics.TotalForwards", "Counts cannot be negative.");
        ValidateCounts(statistics.ServerForwards, "Statistics.ServerForwards");
        ValidateCounts(statistics.ProfileUses, "Statistics.ProfileUses");

        void ValidateCounts(ImmutableDictionary<Guid, long>? counts, string path)
        {
            if (counts is null) { result.Require(false, path, "The counts collection is required."); return; }
            foreach ((Guid id, long count) in counts)
            {
                result.Id(id, path);
                result.Require(count >= 0, path, "Counts cannot be negative.");
            }
        }
    }

    private static void ValidateActivity(ImmutableArray<ActivityEntry> entries, ValidationBuilder result)
    {
        if (entries.IsDefault) { result.Require(false, "RecentActivity", "The recent activity collection is required."); return; }
        result.Require(entries.Length <= AppState.MaximumRecentEntries, "RecentActivity", "Keep at most 200 recent entries; cumulative statistics are separate.");
        var ids = new HashSet<Guid>();
        for (int index = 0; index < entries.Length; index++)
        {
            string path = $"RecentActivity[{index}]";
            ActivityEntry? entry = entries[index];
            if (entry is null) { result.Require(false, path, "An activity entry cannot be null."); continue; }
            result.Id(entry.Id, $"{path}.Id");
            result.Require(ids.Add(entry.Id), $"{path}.Id", "Activity IDs must be unique.");
            result.Utc(entry.OccurredAt, $"{path}.OccurredAt");
            result.Require(Enum.IsDefined(entry.Outcome), $"{path}.Outcome", "Choose a supported outcome.");
            if (entry.Requested is null) { result.Require(false, $"{path}.Requested", "A requested route snapshot is required."); }
            else { result.Add($"{path}.Requested", ConfigurationValidator.ValidateRule(entry.Requested)); }
            result.Require(entry.Source is not null, $"{path}.Source", "A source snapshot is required.");
            if (entry.Source is not null)
            {
                result.Require(!string.IsNullOrWhiteSpace(entry.Source.ServerName), $"{path}.Source.ServerName", "A server name snapshot is required.");
                if (entry.Source.ProfileId is Guid profileId)
                {
                    result.Id(profileId, $"{path}.Source.ProfileId");
                    result.Require(!string.IsNullOrWhiteSpace(entry.Source.ProfileName), $"{path}.Source.ProfileName", "A profile name snapshot is required.");
                }
                if (entry.Source.RuleId is Guid ruleId) { result.Id(ruleId, $"{path}.Source.RuleId"); }
            }
            if (entry.Error is not null)
            {
                result.Require(Enum.IsDefined(entry.Error.Kind), $"{path}.Error.Kind", "Unknown error category.");
                result.Require(!string.IsNullOrWhiteSpace(entry.Error.Message), $"{path}.Error.Message", "An error explanation is required.");
            }
            if (entry.Actual is ForwardRoute route)
            {
                ValidateEndpoint(route.Local, $"{path}.Actual.Local");
                ValidateEndpoint(route.Remote, $"{path}.Actual.Remote");
            }
        }

        void ValidateEndpoint(NetworkEndpoint? endpoint, string path)
        {
            if (endpoint is null) { result.Require(false, path, "An endpoint is required."); return; }
            result.Port(endpoint.Port, $"{path}.Port");
            result.Require(HostAddress.IsValidHost(endpoint.Host), $"{path}.Host", "Enter a valid host.");
        }
    }
}

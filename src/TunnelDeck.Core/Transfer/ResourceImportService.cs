using TunnelDeck.Core.Models;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Transfer;

/// <summary>Append only. Admission and revision checks run inside the same FIFO transaction used by deletion.</summary>
public sealed class ResourceImportService(ConfigurationService configuration, ServerWorkRegistry work, Func<string, bool> keyFileExists)
{
    public Task<ConfigurationCommitResult> CommitAsync(ImportPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (!plan.CanCommit) { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ResourceImportService_ThereAreNoUsableResourcesToImport")); }
        return configuration.UpdateAsync(state =>
        {
            work.RequireResourceAdmission(plan.Servers.Select(server => server.Id), plan.Profiles.Select(profile => profile.Id));
            ValidatePlan(state, plan);
            foreach (RemoteServer server in plan.Servers) { ImportPlanner.RequireKeyFile(server, keyFileExists); }
            return state with
            {
                Servers = state.Servers.AddRange(plan.Servers), Profiles = state.Profiles.AddRange(plan.Profiles),
                Order = new()
                {
                    PinnedServerIds = state.Order.PinnedServerIds.AddRange(plan.Order.PinnedServerIds),
                    OtherServerIds = plan.Order.OtherServerIds.AddRange(state.Order.OtherServerIds),
                    PinnedProfileIds = state.Order.PinnedProfileIds.AddRange(plan.Order.PinnedProfileIds),
                    OtherProfileIds = plan.Order.OtherProfileIds.AddRange(state.Order.OtherProfileIds),
                },
            };
        }, plan.BaseRevision, cancellationToken);
    }

    private static void ValidatePlan(AppState state, ImportPlan plan)
    {
        AppStateValidator.Validate(new() { Servers = plan.Servers, Profiles = plan.Profiles, Order = plan.Order }).ThrowIfInvalid();
        if (plan.Servers.Any(server => server.CredentialReference is not null || server.LastUsedAt is not null || state.Servers.Any(existing => existing.Id == server.Id)) ||
            plan.Profiles.Any(profile => profile.LastUsedAt is not null || state.Profiles.Any(existing => existing.Id == profile.Id)))
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ResourceImportService_PreviewThisFileAgainImportedResourcesMustHave")); }
        var existingRules = state.Profiles.SelectMany(profile => profile.Rules).Select(rule => rule.Id).ToHashSet();
        if (plan.Profiles.SelectMany(profile => profile.Rules).Any(rule => existingRules.Contains(rule.Id)))
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ResourceImportService_PreviewThisFileAgainImportedRulesMustHave")); }
    }
}

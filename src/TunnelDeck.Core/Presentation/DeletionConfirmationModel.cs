using System.Collections.Immutable;
using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Presentation;

public sealed record DeletionProfileGroup(string Heading, string Details, string AutomationId);

public sealed class DeletionConfirmationModel(DeletionImpact impact, string updateNotice = "")
{
    private bool Servers => impact.Request.Kind == ResourceKind.Server;
    private bool Bulk => impact.Request.All || impact.ResourceCount != 1;
    public string Title => Bulk ? impact.Request.All
        ? UiText.Get(Servers ? "Deletion_AllServersTitle" : "Deletion_AllProfilesTitle")
        : UiText.Get("Deletion_SelectedTitle", Count(impact.ResourceCount, Servers ? "server" : "profile")) :
        UiText.Get("DeletionConfirmationModel_Delete_361", (Servers ? ResourceDisplay.ServerName(impact.Servers[0]) : impact.Profiles[0].Name));
    public string ActionLabel => impact.Request.All ? UiText.Get("DeletionConfirmationModel_DeleteAll") : UiText.Get("DeletionConfirmationModel_Delete_363");
    public string UpdateNotice => UiText.Translate(updateNotice);
    public string ResourceSummary => Bulk ? UiText.Get("DeletionConfirmationModel_WillBeDeleted", Count(impact.ResourceCount, Servers ? "server" : "profile")) :
        Servers ? ResourceDisplay.ServerEndpoint(impact.Servers[0]) + " · " + impact.Servers[0].Id.ToString("N")[..8] :
        UiText.Get("DeletionConfirmationModel_TheSavedProfileAndItsRulesWillBe");
    public string RuntimeSummary => impact.Tunnels.IsEmpty ? string.Empty :
        Servers ? UiText.Get("DeletionConfirmationModel_WillBeStopped", Count(impact.Tunnels.Length, "forward")) : UiText.Get("DeletionConfirmationModel_WillKeepRunning", Count(impact.Tunnels.Length, "existing forward"));
    public string PendingSummary => impact.PendingForwardCount == 0 ? string.Empty :
        Servers ? UiText.Get("DeletionConfirmationModel_WillBeCancelled", Count(impact.PendingForwardCount, "pending forward")) :
        UiText.Get("DeletionConfirmationModel_WillContinueFromSavedSettings", Count(impact.PendingForwardCount, "pending forward"));
    public string TestsSummary => impact.ConnectionTestCount == 0 ? string.Empty :
        UiText.Get("DeletionConfirmationModel_WillBeCancelled", Count(impact.ConnectionTestCount, "connection test"));
    public string RulesSummary => !Servers || impact.RuleCount == 0 ? string.Empty :
        UiText.Get("DeletionConfirmationModel_InWillBeRemoved", Count(impact.RuleCount, "forwarding rule"), Count(impact.ProfileCount, "profile"));
    public string KeptProfiles => !Servers || impact.ProfileCount == 0 ? string.Empty : UiText.Get("DeletionConfirmationModel_ProfilesAreKeptIncludingAnyThatBecomeEmpty");
    public string KeptServers => Servers ? string.Empty : UiText.Get("DeletionConfirmationModel_ServersAreKept") +
        (impact.Tunnels.Length + impact.PendingForwardCount > 0 ? UiText.Get("DeletionConfirmationModel_YouCanStopForwardsFromActiveTunnels") : string.Empty);
    public string ClearedSelections => !impact.ClearsDefaultServer && !impact.ClearsPinnedQuickServer ? string.Empty :
        impact.ClearsDefaultServer && impact.ClearsPinnedQuickServer ? UiText.Get("DeletionConfirmationModel_TheDefaultServerAndPinnedQuickForwardSelections") :
        impact.ClearsDefaultServer ? UiText.Get("DeletionConfirmationModel_TheDefaultServerSelectionWillBeCleared") : UiText.Get("DeletionConfirmationModel_ThePinnedQuickForwardSelectionWillBeCleared");
    public bool ShowDetails => Servers && !Bulk && impact.RuleCount > 0;
    public bool ExpandDetails => impact.RuleCount <= 8;
    public ImmutableArray<DeletionProfileGroup> Groups => !ShowDetails ? [] :
        [.. impact.Profiles.Select(profile => new DeletionProfileGroup(profile.Name + " · " + profile.Id.ToString("N")[..8],
            string.Join("\n", profile.Rules.Select(rule => ResourceDisplay.ModeName(rule.Mode) + "  " + ResourceDisplay.FullRoute(rule))),
            "AffectedProfile_" + profile.Id.ToString("N")))];
    private static string Count(int count, string singular) => UiText.Count(count, singular);
}

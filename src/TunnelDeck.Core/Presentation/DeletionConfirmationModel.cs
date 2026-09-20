using System.Collections.Immutable;
using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Presentation;

public sealed record DeletionProfileGroup(string Heading, string Details, string AutomationId);

public sealed class DeletionConfirmationModel(DeletionImpact impact, string updateNotice = "")
{
    private bool Servers => impact.Request.Kind == ResourceKind.Server;
    private bool Bulk => impact.Request.All || impact.ResourceCount != 1;
    public string Title => Bulk ? $"Delete {(impact.Request.All ? "all" : impact.ResourceCount.ToString())} {(Servers ? "servers" : "profiles")}?" :
        $"Delete “{(Servers ? ResourceDisplay.ServerName(impact.Servers[0]) : impact.Profiles[0].Name)}”?";
    public string ActionLabel => impact.Request.All ? "Delete all" : "Delete";
    public string UpdateNotice { get; } = updateNotice;
    public string ResourceSummary => Bulk ? $"{Count(impact.ResourceCount, Servers ? "server" : "profile")} will be deleted." :
        Servers ? ResourceDisplay.ServerEndpoint(impact.Servers[0]) + " · " + impact.Servers[0].Id.ToString("N")[..8] :
        "The saved profile and its rules will be deleted. Any unsaved edits will be discarded.";
    public string RuntimeSummary => impact.Tunnels.IsEmpty ? string.Empty :
        Servers ? $"{Count(impact.Tunnels.Length, "forward")} will be stopped." : $"{Count(impact.Tunnels.Length, "existing forward")} will keep running.";
    public string PendingSummary => impact.PendingForwardCount == 0 ? string.Empty :
        Servers ? $"{Count(impact.PendingForwardCount, "pending forward")} will be cancelled." :
        $"{Count(impact.PendingForwardCount, "pending forward")} will continue from saved settings.";
    public string TestsSummary => impact.ConnectionTestCount == 0 ? string.Empty :
        $"{Count(impact.ConnectionTestCount, "connection test")} will be cancelled.";
    public string RulesSummary => !Servers || impact.RuleCount == 0 ? string.Empty :
        $"{Count(impact.RuleCount, "forwarding rule")} in {Count(impact.ProfileCount, "profile")} will be removed.";
    public string KeptProfiles => !Servers || impact.ProfileCount == 0 ? string.Empty : "Profiles are kept, including any that become empty.";
    public string KeptServers => Servers ? string.Empty : "Servers are kept." +
        (impact.Tunnels.Length + impact.PendingForwardCount > 0 ? " You can stop forwards from Active tunnels." : string.Empty);
    public string ClearedSelections => !impact.ClearsDefaultServer && !impact.ClearsPinnedQuickServer ? string.Empty :
        impact.ClearsDefaultServer && impact.ClearsPinnedQuickServer ? "The default server and pinned Quick Forward selections will be cleared." :
        impact.ClearsDefaultServer ? "The default server selection will be cleared." : "The pinned Quick Forward selection will be cleared.";
    public bool ShowDetails => Servers && !Bulk && impact.RuleCount > 0;
    public bool ExpandDetails => impact.RuleCount <= 8;
    public ImmutableArray<DeletionProfileGroup> Groups => !ShowDetails ? [] :
        [.. impact.Profiles.Select(profile => new DeletionProfileGroup(profile.Name + " · " + profile.Id.ToString("N")[..8],
            string.Join("\n", profile.Rules.Select(rule => ResourceDisplay.ModeName(rule.Mode) + "  " + ResourceDisplay.FullRoute(rule))),
            "AffectedProfile_" + profile.Id.ToString("N")))];
    private static string Count(int count, string singular) => $"{count} {singular}{(count == 1 ? string.Empty : "s")}";
}

using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Deletion;

public sealed record DeletionRequest(ResourceKind Kind, ImmutableArray<Guid> Ids, bool All = false);

public sealed record AffectedProfile(Guid Id, string Name, ImmutableArray<ForwardRule> Rules);

public sealed record DeletionImpact(DeletionRequest Request, long ConfigurationRevision, long RuntimeRevision, long WorkRevision,
    ImmutableArray<RemoteServer> Servers, ImmutableArray<AffectedProfile> Profiles,
    ImmutableArray<ActiveTunnel> Tunnels, ImmutableArray<ServerWorkSnapshot> Work,
    bool ClearsDefaultServer, bool ClearsPinnedQuickServer)
{
    public int ResourceCount => Request.Kind == ResourceKind.Server ? Servers.Length : Profiles.Length;
    public int RuleCount => Profiles.Sum(profile => profile.Rules.Length);
    public int ProfileCount => Profiles.Length;
    public int PendingForwardCount => Work.Sum(work => work.Forwards.Count(rule => rule.TunnelId is not Guid id || !Tunnels.Any(tunnel => tunnel.Id == id)));
    public int ConnectionTestCount => Work.Count(work => work.Kind == ServerWorkKind.ConnectionTest);
}

public enum DeletionOutcome { Deleted, NeedsConfirmation, StopFailed, SaveFailed, Cancelled }

public sealed record DeletionResult(DeletionOutcome Outcome, DeletionImpact Impact, ImmutableArray<Guid> StoppedTunnelIds,
    string Message, AppState? State = null, ImmutableArray<ValidationIssue> Diagnostics = default)
{
    public bool Succeeded => Outcome == DeletionOutcome.Deleted;
}

internal sealed record DeletionPreparation(DeletionResult Result, AppState? Candidate = null);

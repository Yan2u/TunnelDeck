using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Snapshots;

namespace TunnelDeck.Core.Profiles;

public enum ProfileRuleOutcome { Pending, Starting, Started, AlreadyRunning, Failed, Cancelled, AlreadyReconnecting }

public sealed record ProfileRuleResult(RuleLaunchSnapshot Launch, ProfileRuleOutcome Outcome, Guid? TunnelId = null, TunnelFailure? Failure = null);

public sealed record ProfileRunSnapshot(Guid ProfileId, Guid OperationId, string ProfileName, long Revision,
    bool IsBusy, ImmutableArray<ProfileRuleResult> Rules)
{
    public int StartedCount => Rules.Count(rule => rule.Outcome == ProfileRuleOutcome.Started);
    public int AlreadyRunningCount => Rules.Count(rule => rule.Outcome == ProfileRuleOutcome.AlreadyRunning);
    public int AlreadyReconnectingCount => Rules.Count(rule => rule.Outcome == ProfileRuleOutcome.AlreadyReconnecting);
    public int FailedCount => Rules.Count(rule => rule.Outcome == ProfileRuleOutcome.Failed);
    public int CancelledCount => Rules.Count(rule => rule.Outcome == ProfileRuleOutcome.Cancelled);
    public string Summary => string.Join(" · ", new[]
    {
        IsBusy ? $"{Rules.Count(rule => rule.Outcome == ProfileRuleOutcome.Starting)} starting" : null,
        StartedCount > 0 ? $"{StartedCount} started" : null,
        AlreadyRunningCount > 0 ? $"{AlreadyRunningCount} already running" : null,
        AlreadyReconnectingCount > 0 ? $"{AlreadyReconnectingCount} already reconnecting" : null,
        FailedCount > 0 ? $"{FailedCount} failed" : null,
        CancelledCount > 0 ? $"{CancelledCount} cancelled" : null,
    }.Where(text => text is not null));
}

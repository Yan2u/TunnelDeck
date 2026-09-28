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
        IsBusy ? UiText.Get("Diagnostic_ProfileRunSnapshot_Starting", Rules.Count(rule => rule.Outcome == ProfileRuleOutcome.Starting)) : null,
        StartedCount > 0 ? UiText.Get("Diagnostic_ProfileRunSnapshot_Started", StartedCount) : null,
        AlreadyRunningCount > 0 ? UiText.Get("Diagnostic_ProfileRunSnapshot_AlreadyRunning", AlreadyRunningCount) : null,
        AlreadyReconnectingCount > 0 ? UiText.Get("Diagnostic_ProfileRunSnapshot_AlreadyReconnecting", AlreadyReconnectingCount) : null,
        FailedCount > 0 ? UiText.Get("Diagnostic_ProfileRunSnapshot_Failed", FailedCount) : null,
        CancelledCount > 0 ? UiText.Get("Diagnostic_ProfileRunSnapshot_Cancelled", CancelledCount) : null,
    }.Where(text => text is not null));
}

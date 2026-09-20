using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Abstractions;

public sealed record StateLoadResult(AppState? State, ImmutableArray<ValidationIssue> Diagnostics,
    bool IsReadOnly = false, bool RecoveredFromBackup = false);

public interface IStateStore
{
    Task<StateLoadResult> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Checks the expected revision, atomically persists the candidate, and returns the committed revision.</summary>
    Task<AppState> CommitAsync(AppState candidate, long expectedRevision, CancellationToken cancellationToken = default);
}

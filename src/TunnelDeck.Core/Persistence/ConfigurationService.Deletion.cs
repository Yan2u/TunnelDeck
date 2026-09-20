using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Persistence;

public sealed partial class ConfigurationService
{
    /// <summary>Serialize revalidation, stopping and the final candidate. The callback must not ask for input or await another configuration mutation.</summary>
    internal Task<DeletionResult> CommitDeletionAsync(Func<AppState, Task<DeletionPreparation>> prepare,
        CancellationToken cancellationToken) => EnqueueAsync(async () =>
    {
        using IDisposable lease = await transactionLock.AcquireAsync(cancellationToken).ConfigureAwait(false);
        AppState before = RequireWritable(null);
        DeletionPreparation preparation = await prepare(before).ConfigureAwait(false);
        if (preparation.Candidate is null) { return preparation.Result; }
        cancellationToken.ThrowIfCancellationRequested();
        ConfigurationCommitResult commit = await CommitCandidateAsync(before, preparation.Candidate, null, null, cancellationToken).ConfigureAwait(false);
        return preparation.Result with { State = commit.State, Diagnostics = commit.Diagnostics };
    }, cancellationToken);
}

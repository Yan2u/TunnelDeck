using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Persistence;

public sealed partial class ConfigurationService
{
    /// <summary>Apply one setting to the latest configuration without rewriting drafts, rules or resource order.</summary>
    public Task<ConfigurationCommitResult> UpdateSettingsAsync(Func<AppSettings, AppSettings> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        return EnqueueAsync(async () =>
        {
            using IDisposable lease = await transactionLock.AcquireAsync(cancellationToken).ConfigureAwait(false);
            AppState before = RequireWritable(null);
            AppSettings settings = update(before.Settings);
            if (settings == before.Settings) { return new ConfigurationCommitResult(before, []); }
            return await CommitCandidateAsync(before, before with { Settings = settings }, null, null, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }
}

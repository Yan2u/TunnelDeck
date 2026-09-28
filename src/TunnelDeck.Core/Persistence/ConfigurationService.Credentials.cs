using System.Collections.Immutable;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Persistence;

public sealed partial class ConfigurationService
{
    private async Task<ConfigurationCommitResult> CommitCandidateAsync(AppState before, AppState candidate,
        Guid? stagedReference, string? secret, CancellationToken cancellationToken)
    {
        AppStateValidator.Validate(candidate).ThrowIfInvalid();
        if (candidate.Revision != before.Revision) { throw new StateRevisionConflictException(before.Revision, candidate.Revision); }
        try
        {
            if (stagedReference is Guid reference)
            {
                // Journal before CredWrite: a crash at any subsequent point leaves an identifiable orphan.
                await cleanupStore.AddAsync([reference], cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                try { credentials.Write(reference, secret!); }
                catch (Exception)
                {
                    throw new StateStoreException(StateStoreFailure.CredentialWriteFailed, UiText.Invariant("Diagnostic_ConfigurationServiceCredentials_TheCredentialCouldNotBeSavedTheServer"));
                }
            }

            ImmutableHashSet<Guid> removed = References(before).Except(References(candidate));
            if (!removed.IsEmpty) { await cleanupStore.AddAsync(removed, cancellationToken).ConfigureAwait(false); }
            AppState committed = await stateStore.CommitAsync(candidate, before.Revision, cancellationToken).ConfigureAwait(false);

            // From here on, cancellation/cleanup/notification failures cannot change the commit's outcome.
            var loaded = new StateLoadResult(committed, []);
            Volatile.Write(ref _loaded, loaded);
            ImmutableArray<ValidationIssue> diagnostics = await ReconcileCredentialsAsync(loaded, CancellationToken.None).ConfigureAwait(false);
            Volatile.Write(ref _loaded, loaded with { Diagnostics = diagnostics });
            Notify(committed);
            return new(committed, diagnostics);
        }
        catch
        {
            // Read committed references again before compensating; never delete a credential merely on a stale snapshot.
            await ReconcilePersistedCredentialsAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    private async Task<ImmutableArray<ValidationIssue>> ReconcilePersistedCredentialsAsync(CancellationToken cancellationToken)
    {
        try
        {
            StateLoadResult persisted = await stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            return await ReconcileCredentialsAsync(persisted, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return [CleanupPending()]; }
    }

    private async Task<ImmutableArray<ValidationIssue>> ReconcileCredentialsAsync(StateLoadResult loaded, CancellationToken cancellationToken)
    {
        if (loaded.State is null || loaded.IsReadOnly || loaded.RecoveredFromBackup) { return []; }
        try
        {
            ImmutableHashSet<Guid> active = References(loaded.State);
            ImmutableHashSet<Guid> pending = await cleanupStore.ReadAsync(cancellationToken).ConfigureAwait(false);
            bool failed = false;
            foreach (Guid reference in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!active.Contains(reference)) { credentials.Delete(reference); }
                    await cleanupStore.RemoveAsync(reference, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception) { failed = true; }
            }
            return failed ? [CleanupPending()] : [];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception) { return [CleanupPending()]; }
    }

    private static ImmutableHashSet<Guid> References(AppState state) => state.Servers
        .Where(server => server.CredentialReference.HasValue).Select(server => server.CredentialReference!.Value).ToImmutableHashSet();

    private static ValidationIssue CleanupPending() => new("Credentials",
        UiText.Invariant("Diagnostic_ConfigurationServiceCredentials_SomeSavedCredentialsCouldNotBeReconciledTheir"));
}

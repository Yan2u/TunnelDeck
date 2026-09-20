using System.Collections.Immutable;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Persistence;

public sealed record ConfigurationCommitResult(AppState State, ImmutableArray<ValidationIssue> Diagnostics);

/// <summary>FIFO mutations: compute from the latest snapshot, persist, then publish. UI subscribers dispatch to their UI thread.</summary>
public sealed partial class ConfigurationService(IStateStore stateStore, ICredentialStore credentials,
    ICredentialCleanupStore cleanupStore, IConfigurationLock transactionLock) : IShutdownParticipant
{
    private readonly object _queueGate = new();
    private Task _tail = Task.CompletedTask;
    private Task? _shutdown;
    private bool _accepting = true;
    private bool _loadFailed;
    private StateLoadResult? _loaded;

    public StateLoadResult? Loaded => Volatile.Read(ref _loaded);
    public AppState? Current => Loaded?.State;
    public bool IsReadOnly => Volatile.Read(ref _loadFailed) || Loaded is not { State: not null, IsReadOnly: false };
    public event Action<AppState>? StateChanged;

    public Task<StateLoadResult> InitializeAsync(CancellationToken cancellationToken = default) => EnqueueAsync(async () =>
    {
        try
        {
            using IDisposable lease = await transactionLock.AcquireAsync(cancellationToken).ConfigureAwait(false);
            StateLoadResult result = await stateStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            ImmutableArray<ValidationIssue> cleanup = await ReconcileCredentialsAsync(result, cancellationToken).ConfigureAwait(false);
            result = result with { Diagnostics = result.Diagnostics.AddRange(cleanup) };
            Volatile.Write(ref _loaded, result);
            Volatile.Write(ref _loadFailed, false);
            if (result.State is not null) { Notify(result.State); }
            return result;
        }
        catch
        {
            Volatile.Write(ref _loadFailed, true);
            throw;
        }
    }, cancellationToken);

    public Task<ConfigurationCommitResult> UpdateAsync(Func<AppState, AppState> createCandidate,
        long? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(createCandidate);
        return EnqueueAsync(async () =>
        {
            using IDisposable lease = await transactionLock.AcquireAsync(cancellationToken).ConfigureAwait(false);
            AppState before = RequireWritable(expectedRevision);
            AppState candidate = createCandidate(before);
            RequireUnchangedCredentialReferences(before, candidate);
            return await CommitCandidateAsync(before, candidate, null, null, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<ImmutableArray<ValidationIssue>> RetryCredentialCleanupAsync(CancellationToken cancellationToken = default) =>
        EnqueueAsync(async () =>
        {
            using IDisposable lease = await transactionLock.AcquireAsync(cancellationToken).ConfigureAwait(false);
            return await ReconcilePersistedCredentialsAsync(cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>A barrier for all mutations accepted before this call. Edits are already persisted individually.</summary>
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        lock (_queueGate) { return _tail.WaitAsync(cancellationToken); }
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        lock (_queueGate)
        {
            _accepting = false;
            // The caller can stop waiting, but may not cancel a write already past its commit boundary.
            Task pending = _tail;
            _shutdown ??= Task.Run(() => FinishShutdownAsync(pending));
            return _shutdown.WaitAsync(cancellationToken);
        }
    }

    private async Task FinishShutdownAsync(Task pending)
    {
        await pending.ConfigureAwait(false);
        if (Loaded is null) { return; }
        try
        {
            using IDisposable lease = await transactionLock.AcquireAsync().ConfigureAwait(false);
            await ReconcilePersistedCredentialsAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (StateStoreException) { /* The durable journal will be retried on the next successful load. */ }
    }

    private Task<T> EnqueueAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        lock (_queueGate)
        {
            if (!_accepting)
            {
                return Task.FromException<T>(new StateStoreException(StateStoreFailure.ShuttingDown, "TunnelDeck is closing. New edits cannot be saved."));
            }
            Task previous = _tail;
            Task<T> next = Task.Run(async () =>
            {
                await previous.ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return await operation().ConfigureAwait(false);
            });
            _tail = ObserveCompletionAsync(next);
            return next;
        }
    }

    private static async Task ObserveCompletionAsync(Task operation)
    {
        try { await operation.ConfigureAwait(false); }
        catch (Exception) { /* Each request owns its error; one failed edit must not poison the queue. */ }
    }

    private AppState RequireWritable(long? expectedRevision)
    {
        StateLoadResult? loaded = Loaded;
        if (loaded is null) { throw new StateStoreException(StateStoreFailure.NotInitialized, "Load saved data before editing it."); }
        if (IsReadOnly || loaded.State is null) { throw new StateStoreException(StateStoreFailure.ReadOnly, "Saved data is read-only. Resolve the load error before saving."); }
        if (expectedRevision is long expected && loaded.State.Revision != expected)
        {
            throw new StateRevisionConflictException(expected, loaded.State.Revision);
        }
        return loaded.State;
    }

    private static void RequireUnchangedCredentialReferences(AppState before, AppState candidate)
    {
        AppStateValidator.Validate(candidate).ThrowIfInvalid();
        foreach (RemoteServer server in candidate.Servers)
        {
            if (server.CredentialReference is Guid reference &&
                !before.Servers.Any(old => old.Id == server.Id && old.CredentialReference == reference))
            {
                throw new ArgumentException("Use SaveServerAsync to remember a new credential.", nameof(candidate));
            }
        }
    }

    private void Notify(AppState state)
    {
        if (StateChanged is not { } handlers) { return; }
        foreach (Action<AppState> handler in handlers.GetInvocationList().Cast<Action<AppState>>())
        {
            try { handler(state); }
            catch (Exception) { /* An observer cannot turn a successful commit into a failed edit. */ }
        }
    }
}

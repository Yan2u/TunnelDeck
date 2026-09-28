using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;

namespace TunnelDeck.Infrastructure.Persistence;

public sealed class JsonStateStore : IStateStore
{
    private readonly AtomicJsonFile _file;
    private readonly AppStateJsonCodec _codec;

    public JsonStateStore(AppDataDirectory directory) : this(directory.StatePath) { }
    public JsonStateStore(string path, AppStateJsonCodec? codec = null) : this(path, codec, null) { }

    internal JsonStateStore(string path, AppStateJsonCodec? codec, AtomicFileOperations? operations)
    {
        _file = new(path, operations);
        _codec = codec ?? new();
    }

    public async Task<StateLoadResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        using IDisposable lease = await _file.AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await LoadUnderLockAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<AppState> CommitAsync(AppState candidate, long expectedRevision, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        if (candidate.Revision != expectedRevision) { throw new StateRevisionConflictException(expectedRevision, candidate.Revision); }
        using IDisposable lease = await _file.AcquireAsync(cancellationToken).ConfigureAwait(false);
        StateLoadResult loaded = await LoadUnderLockAsync(cancellationToken).ConfigureAwait(false);
        if (loaded.IsReadOnly || loaded.State is null)
        {
            throw new StateStoreException(StateStoreFailure.ReadOnly, UiText.Invariant("Diagnostic_JsonStateStore_SavedDataIsReadOnlyBecauseItsFormat"), loaded.Diagnostics);
        }
        if (loaded.State.Revision != expectedRevision) { throw new StateRevisionConflictException(expectedRevision, loaded.State.Revision); }
        if (expectedRevision == long.MaxValue)
        {
            throw new StateStoreException(StateStoreFailure.ReadOnly, UiText.Invariant("Diagnostic_JsonStateStore_TheSavedRevisionCannotBeAdvancedTheFile"));
        }
        AppState committed = candidate with { Revision = expectedRevision + 1 };
        byte[] bytes = _codec.Encode(committed);
        await _file.WriteAsync(bytes, preserveBackup: loaded.RecoveredFromBackup, cancellationToken).ConfigureAwait(false);
        return committed;
    }

    private async Task<StateLoadResult> LoadUnderLockAsync(CancellationToken cancellationToken)
    {
        byte[]? primary = await _file.ReadAsync(backup: false, cancellationToken).ConfigureAwait(false);
        StateStoreException? primaryError = null;
        if (primary is not null)
        {
            try { return _codec.Decode(primary); }
            catch (StateStoreException exception) when (exception.Failure == StateStoreFailure.UnsupportedSchema)
            {
                return UnsupportedResult();
            }
            catch (StateStoreException exception) when (exception.Failure == StateStoreFailure.InvalidData) { primaryError = exception; }
        }

        byte[]? backup = await _file.ReadAsync(backup: true, cancellationToken).ConfigureAwait(false);
        if (backup is not null)
        {
            try
            {
                StateLoadResult recovered = _codec.Decode(backup);
                return recovered with
                {
                    RecoveredFromBackup = true,
                    Diagnostics = recovered.Diagnostics.Insert(0, new("StateFile",
                        UiText.Invariant("Diagnostic_JsonStateStore_LoadedTheLastValidBackupBecauseTheMain"))),
                };
            }
            catch (StateStoreException exception) when (exception.Failure == StateStoreFailure.UnsupportedSchema) { return UnsupportedResult(); }
            catch (StateStoreException exception) when (exception.Failure == StateStoreFailure.InvalidData)
            {
                throw new StateStoreException(StateStoreFailure.InvalidData,
                    UiText.Invariant("Diagnostic_JsonStateStore_NeitherTheMainConfigurationNorItsBackupCan"),
                    (primaryError?.Diagnostics ?? []).AddRange(exception.Diagnostics));
            }
        }
        if (primaryError is not null) { throw primaryError; }
        return new(new AppState(), []);
    }

    private static StateLoadResult UnsupportedResult() => new(null,
        [new("SchemaVersion", UiText.Invariant("Diagnostic_JsonStateStore_ThisSavedDataFormatIsNotSupportedUpdate"))], IsReadOnly: true);
}

using System.Collections.Immutable;
using System.Text.Json.Serialization;
using TunnelDeck.Core.Abstractions;

namespace TunnelDeck.Infrastructure.Persistence;

public sealed class JsonCredentialCleanupStore : ICredentialCleanupStore
{
    private readonly AtomicJsonFile _file;

    public JsonCredentialCleanupStore(AppDataDirectory directory) => _file = new(directory.CredentialCleanupPath);

    public async Task<ImmutableHashSet<Guid>> ReadAsync(CancellationToken cancellationToken = default)
    {
        using IDisposable lease = await _file.AcquireAsync(cancellationToken).ConfigureAwait(false);
        return await ReadUnderLockAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task AddAsync(IEnumerable<Guid> references, CancellationToken cancellationToken = default)
    {
        ImmutableHashSet<Guid> added = references.ToImmutableHashSet();
        if (added.Contains(Guid.Empty)) { throw new ArgumentException(UiText.Invariant("Diagnostic_JsonCredentialCleanupStore_CredentialReferencesCannotBeEmpty"), nameof(references)); }
        return UpdateAsync(pending => pending.Union(added), cancellationToken);
    }

    public Task RemoveAsync(Guid reference, CancellationToken cancellationToken = default) =>
        UpdateAsync(pending => pending.Remove(reference), cancellationToken);

    private async Task UpdateAsync(Func<ImmutableHashSet<Guid>, ImmutableHashSet<Guid>> update, CancellationToken cancellationToken)
    {
        using IDisposable lease = await _file.AcquireAsync(cancellationToken).ConfigureAwait(false);
        ImmutableHashSet<Guid> before = await ReadUnderLockAsync(cancellationToken).ConfigureAwait(false);
        ImmutableHashSet<Guid> after = update(before);
        if (before.SetEquals(after)) { return; }
        await _file.WriteAsync(VersionedJson.Serialize(new CleanupDocument { References = after.Order().ToImmutableArray() }),
            preserveBackup: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImmutableHashSet<Guid>> ReadUnderLockAsync(CancellationToken cancellationToken)
    {
        byte[]? bytes = await _file.ReadAsync(backup: false, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            if (await _file.ReadAsync(backup: true, cancellationToken).ConfigureAwait(false) is not null) { throw VersionedJson.Invalid(); }
            return ImmutableHashSet<Guid>.Empty;
        }
        using var document = VersionedJson.Parse(bytes);
        if (VersionedJson.ReadSchema(document.RootElement, 1) != 1) { throw VersionedJson.Unsupported(); }
        CleanupDocument data = VersionedJson.Deserialize<CleanupDocument>(document.RootElement);
        if (data.References.IsDefault || data.References.Contains(Guid.Empty) || data.References.Distinct().Count() != data.References.Length)
        {
            throw VersionedJson.Invalid();
        }
        return data.References.ToImmutableHashSet();
    }

    private sealed record CleanupDocument
    {
        [JsonRequired] public int SchemaVersion { get; init; } = 1;
        [JsonRequired] public ImmutableArray<Guid> References { get; init; } = [];
    }
}

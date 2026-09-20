using System.Collections.Immutable;

namespace TunnelDeck.Core.Abstractions;

/// <summary>A durable journal of references to reconcile against committed configuration before deleting.</summary>
public interface ICredentialCleanupStore
{
    Task<ImmutableHashSet<Guid>> ReadAsync(CancellationToken cancellationToken = default);
    Task AddAsync(IEnumerable<Guid> references, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid reference, CancellationToken cancellationToken = default);
}

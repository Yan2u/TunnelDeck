using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;

namespace TunnelDeck.Core.Persistence;

public sealed partial class ConfigurationService
{
    public Task<ConfigurationCommitResult> SaveProfileAsync(ForwardProfile profile, ForwardProfile? original,
        CancellationToken cancellationToken = default) => UpdateAsync(state =>
    {
        ForwardProfile? current = state.Profiles.FirstOrDefault(item => item.Id == profile.Id);
        if (!ProfileEditPolicy.ContentEquals(current, original))
        {
            throw new StateStoreException(StateStoreFailure.RevisionConflict, "This profile changed while you were editing. Reopen it before saving.");
        }
        ForwardProfile saved = profile with { LastUsedAt = current?.LastUsedAt };
        return state with
        {
            Profiles = current is null ? state.Profiles.Add(saved) : state.Profiles.Replace(current, saved),
            Order = current is null ? ResourceOrderService.Add(state.Order, ResourceKind.Profile, saved.Id) : state.Order,
        };
    }, cancellationToken: cancellationToken);

    public Task<ConfigurationCommitResult> ReorderPinnedAsync(ResourceKind kind, IEnumerable<Guid> expected,
        IEnumerable<Guid> reordered, CancellationToken cancellationToken = default)
    {
        ImmutableArray<Guid> before = expected.ToImmutableArray(), after = reordered.ToImmutableArray();
        return UpdateAsync(state =>
        {
            ImmutableArray<Guid> current = kind == ResourceKind.Profile ? state.Order.PinnedProfileIds : state.Order.PinnedServerIds;
            if (!current.SequenceEqual(before))
            {
                throw new StateStoreException(StateStoreFailure.RevisionConflict, "Pinned items changed. Reorder the current list again.");
            }
            return state with { Order = ResourceOrderService.ReorderPinned(state.Order, kind, after) };
        }, cancellationToken: cancellationToken);
    }
}

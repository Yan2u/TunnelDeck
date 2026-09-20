using System.Collections.Immutable;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Persistence;

/// <summary>Repairs derived ordering/default selections only; never drops resources or historical data.</summary>
public static class StateLoadRepair
{
    public static StateLoadResult Apply(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var diagnostics = ImmutableArray.CreateBuilder<ValidationIssue>();
        if (!state.Servers.IsDefault && !state.Profiles.IsDefault && state.Order is not null &&
            state.Servers.All(server => server is not null) && state.Profiles.All(profile => profile is not null))
        {
            Guid[] servers = state.Servers.Select(server => server.Id).ToArray();
            Guid[] profiles = state.Profiles.Select(profile => profile.Id).ToArray();
            if (!servers.Contains(Guid.Empty) && servers.Distinct().Count() == servers.Length &&
                !profiles.Contains(Guid.Empty) && profiles.Distinct().Count() == profiles.Length)
            {
                var serverOrder = RepairOrder(state.Order.PinnedServerIds, state.Order.OtherServerIds, servers, "Order.Servers");
                var profileOrder = RepairOrder(state.Order.PinnedProfileIds, state.Order.OtherProfileIds, profiles, "Order.Profiles");
                state = state with
                {
                    Order = state.Order with
                    {
                        PinnedServerIds = serverOrder.Pinned, OtherServerIds = serverOrder.Other,
                        PinnedProfileIds = profileOrder.Pinned, OtherProfileIds = profileOrder.Other,
                    },
                };
                if (state.Settings is not null)
                {
                    state = state with
                    {
                        Settings = state.Settings with
                        {
                            DefaultServerId = RepairReference(state.Settings.DefaultServerId, servers, "Settings.DefaultServerId"),
                            PinnedQuickServerId = RepairReference(state.Settings.PinnedQuickServerId, servers, "Settings.PinnedQuickServerId"),
                        },
                    };
                }
            }
        }

        ValidationResult validation = AppStateValidator.Validate(state);
        if (!validation.IsValid)
        {
            throw new StateStoreException(StateStoreFailure.InvalidData,
                "Saved configuration contains invalid data. Restore a valid configuration or backup.", validation.Issues);
        }
        return new(state, diagnostics.ToImmutable());

        (ImmutableArray<Guid> Pinned, ImmutableArray<Guid> Other) RepairOrder(
            ImmutableArray<Guid> pinned, ImmutableArray<Guid> other, Guid[] available, string path)
        {
            if (pinned.IsDefault || other.IsDefault) { return (pinned, other); }
            var valid = available.ToHashSet();
            var seen = new HashSet<Guid>();
            ImmutableArray<Guid> fixedPinned = pinned.Where(id => valid.Contains(id) && seen.Add(id)).ToImmutableArray();
            ImmutableArray<Guid> fixedOther = other.Where(id => valid.Contains(id) && seen.Add(id))
                .Concat(available.Where(seen.Add)).ToImmutableArray();
            if (!pinned.SequenceEqual(fixedPinned) || !other.SequenceEqual(fixedOther))
            {
                diagnostics.Add(new(path, "Removed duplicate or missing-resource order entries and appended omitted resources to Other."));
            }
            return (fixedPinned, fixedOther);
        }

        Guid? RepairReference(Guid? reference, Guid[] available, string path)
        {
            if (reference is null || available.Contains(reference.Value)) { return reference; }
            diagnostics.Add(new(path, "Cleared a default selection whose server no longer exists."));
            return null;
        }
    }
}

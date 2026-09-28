using System.Collections.Immutable;
using TunnelDeck.Core.Models;

namespace TunnelDeck.ViewModels;

public sealed partial class ShellViewModel
{
    private IReadOnlyList<ResourceRow> _serverChoices = [];
    private Dictionary<Guid, ResourceRow> _serverRows = [];
    private Dictionary<Guid, ResourceRow>? _profileRows;
    private readonly Dictionary<(ResourceKind Kind, bool Pinned), IReadOnlyList<ResourceRow>> _resourceLists = [];

    private bool RefreshResourceCache(AppState state, bool force = false)
    {
        // Reuse projections across navigation and state commits that leave resources unchanged.
        if (!force && _state.Servers == state.Servers && _state.Profiles == state.Profiles && _state.Order == state.Order) { return false; }
        var pinned = state.Order.PinnedServerIds.ToHashSet();
        _serverChoices = ResourceRow.Disambiguate(state.Servers.Select(server => ResourceRow.FromServer(server, pinned.Contains(server.Id))));
        _serverRows = _serverChoices.ToDictionary(row => row.Id);
        _profileRows = null;
        _resourceLists.Clear();
        return true;
    }

    public IReadOnlyList<ResourceRow> Resources(ResourceKind kind, bool pinned)
    {
        if (_resourceLists.TryGetValue((kind, pinned), out IReadOnlyList<ResourceRow>? cached)) { return cached; }
        ImmutableArray<Guid> order = (kind, pinned) switch
        {
            (ResourceKind.Profile, true) => State.Order.PinnedProfileIds,
            (ResourceKind.Profile, false) => State.Order.OtherProfileIds,
            (ResourceKind.Server, true) => State.Order.PinnedServerIds,
            _ => State.Order.OtherServerIds,
        };
        if (kind == ResourceKind.Profile && _profileRows is null)
        {
            var pinnedProfiles = State.Order.PinnedProfileIds.ToHashSet();
            _profileRows = ResourceRow.Disambiguate(State.Profiles.Select(profile => ResourceRow.FromProfile(profile, pinnedProfiles.Contains(profile.Id))))
                .ToDictionary(row => row.Id);
        }
        Dictionary<Guid, ResourceRow> byId = kind == ResourceKind.Server ? _serverRows : _profileRows!;
        ResourceRow[] rows = order.Select((id, index) => pinned
            ? byId[id] with { CanMoveUp = index > 0, CanMoveDown = index < order.Length - 1 }
            : byId[id]).ToArray();
        _resourceLists.Add((kind, pinned), rows);
        return rows;
    }
}

using System.Collections.Immutable;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Policies;

public sealed record RecentResource(Guid Id, DateTimeOffset? LastUsedAt);
public sealed record SidebarItems(ImmutableArray<Guid> ProfileIds, ImmutableArray<Guid> ServerIds);

public static class SidebarProjection
{
    public static SidebarItems Project(AppState state) => new(
        Select(state.Profiles.Select(profile => new RecentResource(profile.Id, profile.LastUsedAt)), state.Order.PinnedProfileIds, state.Settings.SidebarMaximumItems),
        Select(state.Servers.Select(server => new RecentResource(server.Id, server.LastUsedAt)), state.Order.PinnedServerIds, state.Settings.SidebarMaximumItems));

    public static ImmutableArray<Guid> Select(IEnumerable<RecentResource> resources, IEnumerable<Guid> pinned, int maximumItems)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(pinned);
        if (maximumItems is < AppSettings.MinimumSidebarItems or > AppSettings.MaximumSidebarItems) { throw new ArgumentOutOfRangeException(nameof(maximumItems)); }
        Dictionary<Guid, RecentResource> available = resources.ToDictionary(resource => resource.Id);
        var seen = new HashSet<Guid>();
        var visible = ImmutableArray.CreateBuilder<Guid>();
        foreach (Guid id in pinned)
        {
            if (available.ContainsKey(id) && seen.Add(id)) { visible.Add(id); }
            if (visible.Count == maximumItems) { return visible.ToImmutable(); }
        }
        // A never-used item is not Recent. Equal timestamps use ID so reload/input enumeration cannot reorder ties.
        foreach (RecentResource resource in available.Values.Where(item => item.LastUsedAt.HasValue)
                     .OrderByDescending(item => item.LastUsedAt).ThenBy(item => item.Id))
        {
            if (seen.Add(resource.Id)) { visible.Add(resource.Id); }
            if (visible.Count == maximumItems) { break; }
        }
        return visible.ToImmutable();
    }
}

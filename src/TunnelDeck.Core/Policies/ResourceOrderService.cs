using System.Collections.Immutable;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Policies;

/// <summary>Pure candidate updates. Callers publish an updated order only after persistence succeeds.</summary>
public static class ResourceOrderService
{
    public static ResourceOrder Add(ResourceOrder order, ResourceKind kind, Guid id)
    {
        RequireId(id);
        var (pinned, other) = Get(order, kind);
        return pinned.Contains(id) || other.Contains(id) ? order : With(order, kind, pinned, other.Insert(0, id));
    }

    public static ResourceOrder Pin(ResourceOrder order, ResourceKind kind, Guid id)
    {
        RequireId(id);
        var (pinned, other) = Get(order, kind);
        if (pinned.Contains(id)) { return order; }
        if (!other.Contains(id)) { throw new KeyNotFoundException("The resource is not in this order."); }
        return With(order, kind, pinned.Add(id), other.Remove(id));
    }

    public static ResourceOrder Unpin(ResourceOrder order, ResourceKind kind, Guid id)
    {
        RequireId(id);
        var (pinned, other) = Get(order, kind);
        if (!pinned.Contains(id))
        {
            if (!other.Contains(id)) { throw new KeyNotFoundException("The resource is not in this order."); }
            return order;
        }
        return With(order, kind, pinned.Remove(id), other.Insert(0, id));
    }

    public static ResourceOrder Remove(ResourceOrder order, ResourceKind kind, Guid id)
    {
        RequireId(id);
        var (pinned, other) = Get(order, kind);
        return With(order, kind, pinned.Remove(id), other.Remove(id));
    }

    public static ResourceOrder ReorderPinned(ResourceOrder order, ResourceKind kind, IEnumerable<Guid> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        var (pinned, other) = Get(order, kind);
        ImmutableArray<Guid> replacement = ids.ToImmutableArray();
        if (replacement.Length != pinned.Length || replacement.Distinct().Count() != pinned.Length || !pinned.ToHashSet().SetEquals(replacement))
        {
            throw new ArgumentException("Reordering must preserve exactly the existing Pinned IDs; Other items cannot be dragged into it.", nameof(ids));
        }
        return With(order, kind, replacement, other);
    }

    public static ResourceOrder MovePinned(ResourceOrder order, ResourceKind kind, int fromIndex, int toIndex)
    {
        var (pinned, _) = Get(order, kind);
        if (fromIndex < 0 || fromIndex >= pinned.Length || toIndex < 0 || toIndex >= pinned.Length) { throw new ArgumentOutOfRangeException(nameof(fromIndex)); }
        Guid moved = pinned[fromIndex];
        return ReorderPinned(order, kind, pinned.RemoveAt(fromIndex).Insert(toIndex, moved));
    }

    private static (ImmutableArray<Guid> Pinned, ImmutableArray<Guid> Other) Get(ResourceOrder order, ResourceKind kind)
    {
        ArgumentNullException.ThrowIfNull(order);
        var group = kind switch
        {
            ResourceKind.Profile => (order.PinnedProfileIds, order.OtherProfileIds),
            ResourceKind.Server => (order.PinnedServerIds, order.OtherServerIds),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        if (group.Item1.IsDefault || group.Item2.IsDefault) { throw new ArgumentException("Order collections must be initialized.", nameof(order)); }
        Guid[] all = [.. group.Item1, .. group.Item2];
        if (all.Contains(Guid.Empty) || all.Length != all.Distinct().Count()) { throw new ArgumentException("Order contains empty or duplicate IDs.", nameof(order)); }
        return group;
    }

    private static ResourceOrder With(ResourceOrder order, ResourceKind kind, ImmutableArray<Guid> pinned, ImmutableArray<Guid> other) => kind switch
    {
        ResourceKind.Profile => order with { PinnedProfileIds = pinned, OtherProfileIds = other },
        ResourceKind.Server => order with { PinnedServerIds = pinned, OtherServerIds = other },
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static void RequireId(Guid id)
    {
        if (id == Guid.Empty) { throw new ArgumentException("An ID is required.", nameof(id)); }
    }
}

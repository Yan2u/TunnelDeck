using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class ResourceOrderTests
{
    [Theory]
    [InlineData(ResourceKind.Profile)]
    [InlineData(ResourceKind.Server)]
    public void AddPinMoveAndUnpinPreserveTheirDistinctOrderingRules(ResourceKind kind)
    {
        var original = new ResourceOrder();
        ResourceOrder order = original;
        foreach (int id in new[] { 1, 2, 3 }) { order = ResourceOrderService.Add(order, kind, DomainData.Id(id)); }
        Assert.Equal(new[] { 3, 2, 1 }.Select(DomainData.Id), Other(order, kind));
        order = ResourceOrderService.Pin(order, kind, DomainData.Id(2));
        order = ResourceOrderService.Pin(order, kind, DomainData.Id(1));
        Assert.Equal(new[] { 2, 1 }.Select(DomainData.Id), Pinned(order, kind));
        Assert.Equal(new[] { DomainData.Id(3) }, Other(order, kind));
        order = ResourceOrderService.MovePinned(order, kind, 0, 1);
        Assert.Equal(new[] { 1, 2 }.Select(DomainData.Id), Pinned(order, kind));
        order = ResourceOrderService.Unpin(order, kind, DomainData.Id(1));
        order = ResourceOrderService.Add(order, kind, DomainData.Id(4));
        Assert.Equal(new[] { 4, 1, 3 }.Select(DomainData.Id), Other(order, kind));
        Assert.Equal(new[] { DomainData.Id(2) }, Pinned(order, kind));
        Assert.Same(order, ResourceOrderService.Add(order, kind, DomainData.Id(1)));
        Assert.Same(order, ResourceOrderService.Unpin(order, kind, DomainData.Id(3)));
        Assert.Same(order, ResourceOrderService.Pin(order, kind, DomainData.Id(2)));
        Assert.Empty(Other(original, kind));
        ResourceKind otherKind = kind == ResourceKind.Server ? ResourceKind.Profile : ResourceKind.Server;
        Assert.Empty(Other(order, otherKind));
        Assert.Empty(Pinned(order, otherKind));
    }

    [Fact]
    public void ReorderCannotMoveOtherItemsOrLoseOrDuplicatePinnedItems()
    {
        var order = new ResourceOrder { PinnedProfileIds = [DomainData.Id(1), DomainData.Id(2)], OtherProfileIds = [DomainData.Id(3)] };
        Guid[][] invalid = [[DomainData.Id(1)], [DomainData.Id(1), DomainData.Id(1)], [DomainData.Id(1), DomainData.Id(3)]];
        foreach (Guid[] ids in invalid) { Assert.Throws<ArgumentException>(() => ResourceOrderService.ReorderPinned(order, ResourceKind.Profile, ids)); }
        Assert.Equal(new[] { 1, 2 }.Select(DomainData.Id), order.PinnedProfileIds);
        Assert.Equal(new[] { DomainData.Id(3) }, order.OtherProfileIds);
    }

    [Fact]
    public void OperationsMaintainOneOccurrenceOfEveryResourceAcrossLongSequences()
    {
        var random = new Random(20260919);
        ResourceOrder order = new();
        Guid[] ids = Enumerable.Range(1, 24).Select(DomainData.Id).ToArray();
        foreach (Guid id in ids) { order = ResourceOrderService.Add(order, ResourceKind.Server, id); }
        for (int index = 0; index < 250; index++)
        {
            Guid id = ids[random.Next(ids.Length)];
            order = order.PinnedServerIds.Contains(id)
                ? ResourceOrderService.Unpin(order, ResourceKind.Server, id)
                : ResourceOrderService.Pin(order, ResourceKind.Server, id);
            if (order.PinnedServerIds.Length > 1)
            {
                order = ResourceOrderService.MovePinned(order, ResourceKind.Server, 0, order.PinnedServerIds.Length - 1);
            }
            Guid[] combined = [.. order.PinnedServerIds, .. order.OtherServerIds];
            Assert.Equal(ids.Length, combined.Length);
            Assert.True(ids.ToHashSet().SetEquals(combined));
            Assert.Equal(ids.Length, combined.Distinct().Count());
        }
    }

    [Fact]
    public void RemovalPreservesRelativeOrderInBothGroups()
    {
        var order = new ResourceOrder { PinnedServerIds = [DomainData.Id(1), DomainData.Id(2)], OtherServerIds = [DomainData.Id(3), DomainData.Id(4)] };
        order = ResourceOrderService.Remove(order, ResourceKind.Server, DomainData.Id(1));
        order = ResourceOrderService.Remove(order, ResourceKind.Server, DomainData.Id(4));
        order = ResourceOrderService.Remove(order, ResourceKind.Server, DomainData.Id(99));
        Assert.Equal(new[] { DomainData.Id(2) }, order.PinnedServerIds);
        Assert.Equal(new[] { DomainData.Id(3) }, order.OtherServerIds);
    }

    private static ImmutableArray<Guid> Pinned(ResourceOrder order, ResourceKind kind) => kind == ResourceKind.Server ? order.PinnedServerIds : order.PinnedProfileIds;
    private static ImmutableArray<Guid> Other(ResourceOrder order, ResourceKind kind) => kind == ResourceKind.Server ? order.OtherServerIds : order.OtherProfileIds;
}

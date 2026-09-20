using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class ProfilePersistenceTests
{
    [Fact]
    public async Task SavingPreservesNewUsageAndAllowsUnrelatedConfigurationChanges()
    {
        await using var fixture = await ProfileFixture.CreateAsync(rules: [DomainData.Rule()]);
        var configuration = fixture.Backend.Connections.Configuration;
        ForwardProfile original = fixture.Profile;
        await configuration.UpdateAsync(state => state with
        {
            Profiles = [original with { LastUsedAt = DomainData.Now }],
            Settings = state.Settings with { SidebarMaximumItems = 7 },
        });
        var saved = await configuration.SaveProfileAsync(original with { Name = "Renamed" }, original);
        Assert.Equal("Renamed", saved.State.Profiles.Single().Name);
        Assert.Equal(DomainData.Now, saved.State.Profiles.Single().LastUsedAt);
        Assert.Equal(7, saved.State.Settings.SidebarMaximumItems);
    }

    [Fact]
    public async Task AConflictingEditOrDeletedProfileCannotBeOverwrittenOrRecreated()
    {
        await using var fixture = await ProfileFixture.CreateAsync(rules: [DomainData.Rule()]);
        var configuration = fixture.Backend.Connections.Configuration;
        var original = fixture.Profile;
        await configuration.SaveProfileAsync(original with { Name = "Another edit" }, original);
        var error = await Assert.ThrowsAsync<StateStoreException>(() => configuration.SaveProfileAsync(original with { Name = "Stale" }, original));
        Assert.Equal(StateStoreFailure.RevisionConflict, error.Failure);
        await configuration.UpdateAsync(state => state with { Profiles = [], Order = ResourceOrderService.Remove(state.Order, ResourceKind.Profile, original.Id) });
        await Assert.ThrowsAsync<StateStoreException>(() => configuration.SaveProfileAsync(original, original));
        Assert.Empty(configuration.Current!.Profiles);
    }

    [Fact]
    public async Task FailedSaveKeepsMemoryAndDiskUnchangedAndCanBeRetried()
    {
        await using var fixture = await ProfileFixture.CreateAsync(rules: [DomainData.Rule()]);
        var backend = fixture.Backend.Connections;
        AppState before = backend.Configuration.Current!;
        backend.Store.BeforeCommit = () => Task.FromException(new IOException("Synthetic write failure."));
        await Assert.ThrowsAsync<IOException>(() => backend.Configuration.SaveProfileAsync(fixture.Profile with { Name = "Retry" }, fixture.Profile));
        Assert.Same(before, backend.Configuration.Current); Assert.Equal(before, backend.Store.State);
        backend.Store.BeforeCommit = null;
        await backend.Configuration.SaveProfileAsync(fixture.Profile with { Name = "Retry" }, fixture.Profile);
        Assert.Equal("Retry", backend.Store.State.Profiles.Single().Name);
    }

    [Theory]
    [InlineData(ResourceKind.Server)]
    [InlineData(ResourceKind.Profile)]
    public async Task ReorderingOnlyAcceptsTheCapturedPinnedSetAndRetriesAfterStorageFailure(ResourceKind kind)
    {
        await using var fixture = await ProfileFixture.CreateAsync();
        var backend = fixture.Backend.Connections;
        await backend.Configuration.SaveProfileAsync(DomainData.Profile(2), null);
        Guid first = kind == ResourceKind.Server ? DomainData.Id(1) : fixture.Profile.Id;
        Guid second = kind == ResourceKind.Server ? DomainData.Id(2) : DomainData.Profile(2).Id;
        await backend.Configuration.UpdateAsync(state => state with { Order = ResourceOrderService.Pin(ResourceOrderService.Pin(state.Order, kind, first), kind, second) });
        AppState before = backend.Configuration.Current!;
        backend.Store.BeforeCommit = () => Task.FromException(new IOException("Synthetic write failure."));
        await Assert.ThrowsAsync<IOException>(() => backend.Configuration.ReorderPinnedAsync(kind, [first, second], [second, first]));
        Assert.Same(before, backend.Configuration.Current);
        backend.Store.BeforeCommit = null;
        await backend.Configuration.UpdateAsync(state => state with { Settings = state.Settings with { SidebarMaximumItems = 6 } });
        var saved = await backend.Configuration.ReorderPinnedAsync(kind, [first, second], [second, first]);
        Assert.Equal(new[] { second, first }, kind == ResourceKind.Server ? saved.State.Order.PinnedServerIds : saved.State.Order.PinnedProfileIds);
        await Assert.ThrowsAsync<StateStoreException>(() => backend.Configuration.ReorderPinnedAsync(kind, [first, second], [second, first]));
        await Assert.ThrowsAsync<ArgumentException>(() => backend.Configuration.ReorderPinnedAsync(kind, [second, first], [second, DomainData.Id(99)]));
    }
}

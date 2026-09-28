using System.Collections.Immutable;
using System.Globalization;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Validation;
using TunnelDeck.Infrastructure.Persistence;
using TunnelDeck.Infrastructure.Tests.Fixtures;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class SettingsAndActivityPersistenceTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(10)]
    public async Task SettingsBoundariesPersistWithoutChangingRulesOrResourceOrder(int maximum)
    {
        using var fixture = new StorageFixture();
        var service = fixture.Service();
        await service.InitializeAsync();
        await service.SaveServerAsync(StorageFixture.Server(), CredentialChange.Forget);
        AppState before = service.Current!;
        await Task.WhenAll(
            service.UpdateSettingsAsync(settings => settings with { SidebarMaximumItems = maximum }),
            service.UpdateSettingsAsync(settings => settings with { AutoFindAvailablePortDefault = false }),
            service.UpdateSettingsAsync(settings => settings with { DefaultServerId = before.Servers[0].Id }));
        var reopened = fixture.Service();
        await reopened.InitializeAsync();
        Assert.Equal(maximum, reopened.Current!.Settings.SidebarMaximumItems);
        Assert.False(reopened.Current.Settings.AutoFindAvailablePortDefault);
        Assert.Equal(before.Servers[0].Id, reopened.Current.Settings.DefaultServerId);
        Assert.Equal(before.Order.OtherServerIds.ToArray(), reopened.Current.Order.OtherServerIds.ToArray());
        Assert.Equal(before.Profiles.ToArray(), reopened.Current.Profiles.ToArray());
        Assert.Equal(before.Statistics.TotalForwards, reopened.Current.Statistics.TotalForwards);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(11)]
    public async Task InvalidRangeOrDeletedDefaultCannotBeSaved(int maximum)
    {
        using var fixture = new StorageFixture();
        var service = fixture.Service();
        await service.InitializeAsync();
        await Assert.ThrowsAsync<DomainValidationException>(() => service.UpdateSettingsAsync(settings => settings with { SidebarMaximumItems = maximum }));
        await Assert.ThrowsAsync<DomainValidationException>(() => service.UpdateSettingsAsync(settings => settings with { DefaultServerId = Guid.NewGuid() }));
        Assert.Equal(0, service.Current!.Revision);
    }

    [Fact]
    public async Task FailureKeepsSavedSettingsRetryCommitsOnceAndNoOpDoesNotWrite()
    {
        using var fixture = new StorageFixture();
        var operations = new FaultingFileOperations { Fault = FileFault.Flush };
        var service = fixture.Service(new JsonStateStore(fixture.Directory.StatePath, null, operations));
        await service.InitializeAsync();
        // A no-op must not touch a failing disk or invalidate an import preview.
        var unchanged = await service.UpdateSettingsAsync(settings => settings with { SidebarMaximumItems = 5 });
        Assert.Equal(0, unchanged.State.Revision);
        await Assert.ThrowsAsync<StateStoreException>(() => service.UpdateSettingsAsync(settings => settings with { SidebarMaximumItems = 3 }));
        Assert.Equal(5, service.Current!.Settings.SidebarMaximumItems);
        operations.Fault = FileFault.None;
        await service.UpdateSettingsAsync(settings => settings with { SidebarMaximumItems = 3 });
        await service.UpdateSettingsAsync(settings => settings with { SidebarMaximumItems = 3 });
        Assert.Equal(1, service.Current.Revision);
        Assert.Equal(3, (await fixture.Store.LoadAsync()).State!.Settings.SidebarMaximumItems);
    }

    [Fact]
    public async Task LifetimeCountsAndHistoricalSnapshotsSurviveRestartWithOnlyTwoHundredEvents()
    {
        using var fixture = new StorageFixture();
        var service = fixture.Service();
        await service.InitializeAsync();
        AppState sample = StorageFixture.CompleteState();
        sample = sample with
        {
            Servers = [.. sample.Servers.Select(server => server with { CredentialReference = null })],
            RecentActivity = [.. Enumerable.Range(0, 200).Select(index => sample.RecentActivity[0] with
            { Id = Guid.NewGuid(), OccurredAt = StorageFixture.Now.AddMinutes(-index) })],
            Statistics = sample.Statistics with { TotalForwards = 9000 },
        };
        await service.UpdateAsync(_ => sample);
        var reopened = fixture.Service();
        await reopened.InitializeAsync();
        AppState restored = reopened.Current!;
        Assert.Equal(200, restored.RecentActivity.Length);
        Assert.Equal(9000, restored.Statistics.TotalForwards);
        Assert.All(restored.RecentActivity, item => Assert.Equal(TimeSpan.Zero, item.OccurredAt.Offset));
        var row = new RecentActivityItem(restored.RecentActivity[0], restored);
        Assert.Equal("Deleted profile", row.Name);
        Assert.Contains("Server deleted", row.SourceNote);
        Assert.Equal("9,000", UsageStatisticsProjection.Create(restored, CultureInfo.InvariantCulture).TotalForwards);
    }
}

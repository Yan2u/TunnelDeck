using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Policies;
using TunnelDeck.Infrastructure.Persistence;
using TunnelDeck.Infrastructure.Tests.Fixtures;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class ConfigurationQueueTests
{
    [Fact]
    public async Task ConcurrentEditsAreFifoAndBuildOnEveryPreviousCommit()
    {
        using var fixture = new StorageFixture();
        ConfigurationService service = fixture.Service();
        await service.InitializeAsync();
        Task<ConfigurationCommitResult>[] edits = Enumerable.Range(0, 24).Select(_ => service.UpdateAsync(state =>
            state with { Statistics = state.Statistics with { TotalForwards = state.Statistics.TotalForwards + 1 } })).ToArray();
        ConfigurationCommitResult[] results = await Task.WhenAll(edits);
        Assert.Equal(Enumerable.Range(1, 24).Select(value => (long)value), results.Select(result => result.State.Revision));
        Assert.Equal(24, service.Current!.Statistics.TotalForwards);
        Assert.Equal(24, (await fixture.Store.LoadAsync()).State!.Statistics.TotalForwards);
    }

    [Fact]
    public async Task IndependentStoreInstancesCannotLoseAnUpdateWithTheSameExpectedRevision()
    {
        using var fixture = new StorageFixture();
        Task<bool>[] attempts = Enumerable.Range(0, 12).Select(async _ =>
        {
            try { await fixture.Store.CommitAsync(new(), 0); return true; }
            catch (StateRevisionConflictException exception)
            {
                Assert.Equal(0, exception.ExpectedRevision);
                Assert.Equal(1, exception.ActualRevision);
                return false;
            }
        }).ToArray();
        Assert.Single(await Task.WhenAll(attempts), success => success);
        Assert.Equal(1, (await fixture.Store.LoadAsync()).State!.Revision);
    }

    [Fact]
    public async Task FailedEditDoesNotPublishAndDoesNotPoisonLaterEdits()
    {
        using var fixture = new StorageFixture();
        var operations = new FaultingFileOperations { Fault = FileFault.Flush };
        ConfigurationService service = fixture.Service(new JsonStateStore(fixture.Directory.StatePath, null, operations));
        await service.InitializeAsync();
        int published = 0;
        service.StateChanged += _ => published++;
        await Assert.ThrowsAsync<StateStoreException>(() => service.UpdateAsync(state => state with { Settings = state.Settings with { SidebarMaximumItems = 3 } }));
        Assert.Equal(0, published);
        Assert.Equal(0, service.Current!.Revision);
        Assert.Equal(5, service.Current.Settings.SidebarMaximumItems);
        operations.Fault = FileFault.None;
        await service.UpdateAsync(state => state with { Settings = state.Settings with { SidebarMaximumItems = 7 } });
        Assert.Equal(1, published);
        Assert.Equal(1, service.Current.Revision);
        Assert.Equal(7, (await fixture.Store.LoadAsync()).State!.Settings.SidebarMaximumItems);
    }

    [Fact]
    public async Task ShutdownWaitsForAllAcceptedEditsAndRejectsNewOnes()
    {
        using var fixture = new StorageFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new FaultingFileOperations { BeforeFlush = () => { entered.TrySetResult(); return release.Task; } };
        ConfigurationService service = fixture.Service(new JsonStateStore(fixture.Directory.StatePath, null, operations));
        await service.InitializeAsync();
        Task first = service.UpdateAsync(Increment);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task second = service.UpdateAsync(Increment);
        Task flush = service.FlushAsync();
        using var deadline = new CancellationTokenSource();
        Task shutdownWait = service.ShutdownAsync(deadline.Token);
        try
        {
            Assert.False(flush.IsCompleted);
            Assert.False(shutdownWait.IsCompleted);
            Assert.Equal(0, service.Current!.Revision);
            var error = await Assert.ThrowsAsync<StateStoreException>(() => service.UpdateAsync(Increment));
            Assert.Equal(StateStoreFailure.ShuttingDown, error.Failure);
            deadline.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => shutdownWait);
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(first, second, flush);
        await service.ShutdownAsync(CancellationToken.None);
        Assert.Equal(2, service.Current!.Revision);
        Assert.Equal(2, (await fixture.Store.LoadAsync()).State!.Statistics.TotalForwards);
    }

    [Fact]
    public async Task CancelledQueuedEditNeverRunsAndLaterEditsStillCommit()
    {
        using var fixture = new StorageFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new FaultingFileOperations { BeforeFlush = () => { entered.TrySetResult(); return release.Task; } };
        ConfigurationService service = fixture.Service(new JsonStateStore(fixture.Directory.StatePath, null, operations));
        await service.InitializeAsync();
        Task first = service.UpdateAsync(Increment);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        bool called = false;
        Task cancelled = service.UpdateAsync(state => { called = true; return Increment(state); }, cancellationToken: cancellation.Token);
        cancellation.Cancel();
        release.TrySetResult();
        await first;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        await service.UpdateAsync(Increment);
        Assert.False(called);
        Assert.Equal(2, service.Current!.Revision);
    }

    [Fact]
    public async Task ExternalRevisionConflictKeepsThePublishedSnapshotUntilExplicitReload()
    {
        using var fixture = new StorageFixture();
        ConfigurationService service = fixture.Service();
        await service.InitializeAsync();
        AppState outside = await fixture.Store.CommitAsync(Increment(new()), 0);
        await Assert.ThrowsAsync<StateRevisionConflictException>(() => service.UpdateAsync(Increment));
        Assert.Equal(0, service.Current!.Revision);
        Assert.Equal(outside.Revision, (await fixture.Store.LoadAsync()).State!.Revision);
        await service.InitializeAsync();
        await service.UpdateAsync(Increment);
        Assert.Equal(2, service.Current.Statistics.TotalForwards);
        await Assert.ThrowsAsync<StateRevisionConflictException>(() => service.UpdateAsync(Increment, expectedRevision: 1));
    }

    [Fact]
    public async Task SubscriberFailureCannotChangeSuccessfulCommitOutcome()
    {
        using var fixture = new StorageFixture();
        ConfigurationService service = fixture.Service();
        await service.InitializeAsync();
        service.StateChanged += _ => throw new InvalidOperationException("Injected observer failure.");
        long observed = 0;
        service.StateChanged += state => observed = state.Revision;
        ConfigurationCommitResult result = await service.UpdateAsync(Increment);
        Assert.Equal(1, result.State.Revision);
        Assert.Equal(1, observed);
        Assert.Equal(1, (await fixture.Store.LoadAsync()).State!.Revision);
    }

    [Fact]
    public async Task PinAndSettingsUseTheSameDurableCommitPath()
    {
        using var fixture = new StorageFixture();
        ConfigurationService service = fixture.Service();
        await service.InitializeAsync();
        var server = StorageFixture.Server();
        await service.SaveServerAsync(server, CredentialChange.Forget);
        await service.UpdateAsync(state => state with
        {
            Order = ResourceOrderService.Pin(state.Order, ResourceKind.Server, server.Id),
            Settings = state.Settings with { DefaultServerId = server.Id },
        });
        var reopened = fixture.Service();
        await reopened.InitializeAsync();
        Assert.Equal([server.Id], reopened.Current!.Order.PinnedServerIds.ToArray());
        Assert.Empty(reopened.Current.Order.OtherServerIds);
        Assert.Equal(server.Id, reopened.Current.Settings.DefaultServerId);
        Assert.Equal(2, reopened.Current.Revision);
    }

    [Fact]
    public async Task ReadOnlyLoadAndFailedReloadPreventWrites()
    {
        using var fixture = new StorageFixture();
        System.IO.Directory.CreateDirectory(fixture.Directory.RootPath);
        await File.WriteAllTextAsync(fixture.Directory.StatePath, "{\"schemaVersion\":2}");
        ConfigurationService service = fixture.Service();
        Assert.True((await service.InitializeAsync()).IsReadOnly);
        Assert.Null(service.Current);
        await Assert.ThrowsAsync<StateStoreException>(() => service.UpdateAsync(Increment));
        await File.WriteAllTextAsync(fixture.Directory.StatePath, "{}");
        await Assert.ThrowsAsync<StateStoreException>(() => service.InitializeAsync());
        Assert.True(service.IsReadOnly);
    }

    [Fact]
    public async Task ConfigurationLeaseWaitCanBeCancelledWithoutStealingOwnership()
    {
        using var fixture = new StorageFixture();
        var fileLock = new FileConfigurationLock(fixture.Directory.TransactionLockPath);
        using IDisposable first = await fileLock.AcquireAsync();
        using var cancellation = new CancellationTokenSource();
        Task<IDisposable> waiting = fileLock.AcquireAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
    }

    private static AppState Increment(AppState state) => state with
    {
        Statistics = state.Statistics with { TotalForwards = state.Statistics.TotalForwards + 1 },
    };
}

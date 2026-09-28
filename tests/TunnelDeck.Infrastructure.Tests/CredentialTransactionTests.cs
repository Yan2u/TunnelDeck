using System.Text.Json;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Policies;
using TunnelDeck.Infrastructure.Persistence;
using TunnelDeck.Infrastructure.Security;
using TunnelDeck.Infrastructure.Tests.Fixtures;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class CredentialTransactionTests
{
    [Fact]
    public async Task AnotherInstanceCannotCleanAStagedCredentialBeforeItsConfigurationCommits()
    {
        using var fixture = new StorageFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operations = new FaultingFileOperations { BeforeFlush = () => { entered.TrySetResult(); return release.Task; } };
        ConfigurationService first = fixture.Service(new JsonStateStore(fixture.Directory.StatePath, null, operations));
        await first.InitializeAsync();
        Task<ConfigurationCommitResult> save = first.SaveServerAsync(StorageFixture.Server(), CredentialChange.Remember("fixture-in-progress"));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = new ConfigurationService(fixture.Store, fixture.Credentials, fixture.Cleanup,
            new ObservedLock(new FileConfigurationLock(fixture.Directory.TransactionLockPath), attempted));
        Task<StateLoadResult> load = second.InitializeAsync();
        try
        {
            await attempted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(load.IsCompleted);
            Assert.Single(await fixture.Cleanup.ReadAsync());
            Assert.Single(fixture.Credentials.Values);
        }
        finally { release.TrySetResult(); }
        var committed = await save;
        await load;
        Guid reference = committed.State.Servers[0].CredentialReference!.Value;
        Assert.Equal(reference, second.Current!.Servers[0].CredentialReference);
        Assert.NotNull(fixture.Credentials.Read(reference));
    }

    [Fact]
    public async Task RememberReplaceForgetUsesFreshReferencesAndNeverPersistsSecrets()
    {
        using var fixture = new StorageFixture();
        ConfigurationService service = fixture.Service();
        await service.InitializeAsync();
        string firstSecret = "test-secret-" + Guid.NewGuid();
        string nextSecret = "test-secret-" + Guid.NewGuid();
        RemoteServer server = StorageFixture.Server();
        var first = await service.SaveServerAsync(server, CredentialChange.Remember(firstSecret));
        Guid oldReference = first.State.Servers[0].CredentialReference!.Value;
        bool oldExistedUntilCommit = false;
        fixture.Credentials.OnWrite = (_, _) => oldExistedUntilCommit = fixture.Credentials.Read(oldReference) is not null;
        var second = await service.SaveServerAsync(server, CredentialChange.Remember(nextSecret));
        Guid newReference = second.State.Servers[0].CredentialReference!.Value;
        Assert.True(oldExistedUntilCommit);
        Assert.NotEqual(oldReference, newReference);
        Assert.Null(fixture.Credentials.Read(oldReference));
        Assert.True(fixture.Credentials.Read(newReference) == nextSecret, "The replacement credential must round-trip.");
        foreach (string path in System.IO.Directory.GetFiles(fixture.Directory.RootPath, "*.json*"))
        {
            string text = await File.ReadAllTextAsync(path);
            Assert.False(text.Contains(firstSecret, StringComparison.Ordinal), "A secret leaked to disk.");
            Assert.False(text.Contains(nextSecret, StringComparison.Ordinal), "A secret leaked to disk.");
        }
        CredentialChange change = CredentialChange.Remember(firstSecret);
        Assert.False(change.ToString().Contains(firstSecret, StringComparison.Ordinal), "A secret leaked to diagnostic formatting.");
        Assert.False(JsonSerializer.Serialize(change).Contains(firstSecret, StringComparison.Ordinal), "A secret leaked to serialization.");
        await service.SaveServerAsync(server, CredentialChange.Forget);
        Assert.Null(service.Current!.Servers[0].CredentialReference);
        Assert.Empty(fixture.Credentials.Values);
        Assert.Empty(await fixture.Cleanup.ReadAsync());
    }

    [Theory]
    [InlineData(FileFault.PartialWrite)]
    [InlineData(FileFault.Flush)]
    [InlineData(FileFault.Publish)]
    public async Task FailedConfigurationReplacementCompensatesOnlyTheNewCredential(FileFault fault)
    {
        using var fixture = new StorageFixture();
        var operations = new FaultingFileOperations();
        ConfigurationService service = fixture.Service(new JsonStateStore(fixture.Directory.StatePath, null, operations));
        await service.InitializeAsync();
        RemoteServer server = StorageFixture.Server();
        await service.SaveServerAsync(server, CredentialChange.Remember("fixture-initial"));
        Guid original = service.Current!.Servers[0].CredentialReference!.Value;
        operations.Fault = fault;
        await Assert.ThrowsAsync<StateStoreException>(() => service.SaveServerAsync(server, CredentialChange.Remember("fixture-replacement")));
        Assert.Equal(original, service.Current.Servers[0].CredentialReference);
        Assert.Equal(original, (await fixture.Store.LoadAsync()).State!.Servers[0].CredentialReference);
        Assert.Single(fixture.Credentials.Values);
        Assert.True(fixture.Credentials.Values.ContainsKey(original));
        Assert.Empty(await fixture.Cleanup.ReadAsync());
    }

    [Fact]
    public async Task CancellationAfterCredentialWriteCompensatesBeforeReturning()
    {
        using var fixture = new StorageFixture();
        ConfigurationService service = fixture.Service();
        await service.InitializeAsync();
        using var cancellation = new CancellationTokenSource();
        fixture.Credentials.OnWrite = (_, _) => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveServerAsync(StorageFixture.Server(),
            CredentialChange.Remember("fixture-cancelled"), cancellationToken: cancellation.Token));
        Assert.Empty(fixture.Credentials.Values);
        Assert.Empty(service.Current!.Servers);
        Assert.Empty(await fixture.Cleanup.ReadAsync());
    }

    [Fact]
    public async Task CredentialAdapterFailureIsCompensatedAndItsRawMessageIsNeverExposed()
    {
        using var fixture = new StorageFixture();
        ConfigurationService service = fixture.Service();
        await service.InitializeAsync();
        string secret = "test-secret-" + Guid.NewGuid();
        fixture.Credentials.OnWrite = (_, _) => throw new IOException(secret);
        StateStoreException error = await Assert.ThrowsAsync<StateStoreException>(() => service.SaveServerAsync(StorageFixture.Server(), CredentialChange.Remember(secret)));
        Assert.Equal(StateStoreFailure.CredentialWriteFailed, error.Failure);
        Assert.False(error.ToString().Contains(secret, StringComparison.Ordinal), "An adapter exception leaked a secret.");
        Assert.Empty(fixture.Credentials.Values);
        Assert.Equal(0, service.Current!.Revision);
    }

    [Fact]
    public async Task FailedPostCommitCleanupIsDurableAndRetriedAfterRestart()
    {
        using var fixture = new StorageFixture();
        ConfigurationService service = fixture.Service();
        await service.InitializeAsync();
        var server = StorageFixture.Server();
        await service.SaveServerAsync(server, CredentialChange.Remember("fixture-first"));
        Guid oldReference = service.Current!.Servers[0].CredentialReference!.Value;
        fixture.Credentials.OnDelete = _ => throw new IOException("Injected credential deletion failure.");
        ConfigurationCommitResult saved = await service.SaveServerAsync(server, CredentialChange.Remember("fixture-next"));
        Guid currentReference = saved.State.Servers[0].CredentialReference!.Value;
        Assert.Equal(2, saved.State.Revision);
        Assert.Contains(saved.Diagnostics, issue => issue.Path == "Credentials");
        Assert.Contains(oldReference, await fixture.Cleanup.ReadAsync());
        fixture.Credentials.OnDelete = null;
        ConfigurationService restarted = fixture.Service();
        await restarted.InitializeAsync();
        Assert.Null(fixture.Credentials.Read(oldReference));
        Assert.NotNull(fixture.Credentials.Read(currentReference));
        Assert.Empty(await fixture.Cleanup.ReadAsync());
    }

    [Fact]
    public async Task FailedCompensationRemainsRetryableAcrossRestart()
    {
        using var fixture = new StorageFixture();
        var store = new JsonStateStore(fixture.Directory.StatePath, null, new FaultingFileOperations { Fault = FileFault.Flush });
        ConfigurationService service = fixture.Service(store);
        await service.InitializeAsync();
        fixture.Credentials.OnDelete = _ => throw new IOException("Injected deletion failure.");
        await Assert.ThrowsAsync<StateStoreException>(() => service.SaveServerAsync(StorageFixture.Server(), CredentialChange.Remember("fixture-orphan")));
        Assert.Single(fixture.Credentials.Values);
        Assert.Single(await fixture.Cleanup.ReadAsync());
        fixture.Credentials.OnDelete = null;
        await fixture.Service().InitializeAsync();
        Assert.Empty(fixture.Credentials.Values);
        Assert.Empty(await fixture.Cleanup.ReadAsync());
    }

    [Fact]
    public async Task StartupReconcilesInterruptedTransactionsWithoutDeletingLiveReferences()
    {
        using var fixture = new StorageFixture();
        Guid live = StorageFixture.Id(800);
        Guid orphan = StorageFixture.Id(801);
        RemoteServer server = StorageFixture.Server() with { CredentialReference = live };
        await fixture.Store.CommitAsync(new AppState { Servers = [server], Order = new() { OtherServerIds = [server.Id] } }, 0);
        fixture.Credentials.Write(live, "fixture-live");
        fixture.Credentials.Write(orphan, "fixture-orphan");
        await fixture.Cleanup.AddAsync([live, orphan]);
        await fixture.Service().InitializeAsync();
        Assert.NotNull(fixture.Credentials.Read(live));
        Assert.Null(fixture.Credentials.Read(orphan));
        Assert.Empty(await fixture.Cleanup.ReadAsync());
    }

    [Fact]
    public async Task UnknownConfigurationNeverTriggersCredentialDeletion()
    {
        using var fixture = new StorageFixture();
        Guid reference = Guid.NewGuid();
        fixture.Credentials.Write(reference, "fixture-protected");
        await fixture.Cleanup.AddAsync([reference]);
        await File.WriteAllTextAsync(fixture.Directory.StatePath, $"{{\"schemaVersion\":{AppState.CurrentSchemaVersion + 1}}}");
        await fixture.Service().InitializeAsync();
        Assert.NotNull(fixture.Credentials.Read(reference));
        Assert.Contains(reference, await fixture.Cleanup.ReadAsync());
        Assert.Empty(fixture.Credentials.Deleted);
    }

    [Fact]
    public async Task SharedReferencesAreDeletedOnlyAfterTheLastServerForgetsThem()
    {
        using var fixture = new StorageFixture();
        Guid reference = Guid.NewGuid();
        RemoteServer first = StorageFixture.Server(1) with { CredentialReference = reference };
        RemoteServer second = StorageFixture.Server(2) with { CredentialReference = reference };
        await fixture.Store.CommitAsync(new AppState { Servers = [first, second], Order = new() { OtherServerIds = [first.Id, second.Id] } }, 0);
        fixture.Credentials.Write(reference, "fixture-shared");
        ConfigurationService service = fixture.Service();
        await service.InitializeAsync();
        await service.SaveServerAsync(first, CredentialChange.Forget);
        Assert.NotNull(fixture.Credentials.Read(reference));
        await service.UpdateAsync(state => state with
        {
            Servers = state.Servers.RemoveAt(1), Order = ResourceOrderService.Remove(state.Order, ResourceKind.Server, second.Id),
        });
        Assert.Null(fixture.Credentials.Read(reference));
    }

    [Fact]
    public async Task BrokenJournalBlocksNewCredentialsBeforeCredWriteAndRetainsConfiguration()
    {
        using var fixture = new StorageFixture();
        System.IO.Directory.CreateDirectory(fixture.Directory.RootPath);
        await File.WriteAllTextAsync(fixture.Directory.CredentialCleanupPath, "{}");
        ConfigurationService service = fixture.Service();
        Assert.Contains((await service.InitializeAsync()).Diagnostics, issue => issue.Path == "Credentials");
        await Assert.ThrowsAsync<StateStoreException>(() => service.SaveServerAsync(StorageFixture.Server(), CredentialChange.Remember("fixture-secret")));
        Assert.Empty(fixture.Credentials.Values);
        Assert.Empty(service.Current!.Servers);
        Assert.Equal("{}", await File.ReadAllTextAsync(fixture.Directory.CredentialCleanupPath));
    }

    [Fact]
    public async Task AtomicPublicationCancellationDoesNotDeleteTheNowReferencedCredential()
    {
        using var fixture = new StorageFixture();
        using var cancellation = new CancellationTokenSource();
        var store = new JsonStateStore(fixture.Directory.StatePath, null, new FaultingFileOperations { AfterPublish = cancellation.Cancel });
        ConfigurationService service = fixture.Service(store);
        await service.InitializeAsync();
        var saved = await service.SaveServerAsync(StorageFixture.Server(), CredentialChange.Remember("fixture-committed"), cancellationToken: cancellation.Token);
        Guid reference = saved.State.Servers[0].CredentialReference!.Value;
        Assert.NotNull(fixture.Credentials.Read(reference));
        Assert.Equal(reference, (await fixture.Store.LoadAsync()).State!.Servers[0].CredentialReference);
        Assert.Empty(await fixture.Cleanup.ReadAsync());
    }

    [Fact]
    public async Task IsolatedWindowsCredentialsWorkThroughTheRealPersistenceTransaction()
    {
        using var fixture = new StorageFixture();
        var credentials = new WindowsCredentialStore("TunnelDeck/Tests/" + Guid.NewGuid().ToString("N"));
        ConfigurationService service = fixture.Service(credentials: credentials);
        await service.InitializeAsync();
        Guid? reference = null;
        string secret = "test-only-测试-" + Guid.NewGuid();
        try
        {
            var saved = await service.SaveServerAsync(StorageFixture.Server(), CredentialChange.Remember(secret));
            reference = saved.State.Servers[0].CredentialReference;
            Assert.True(credentials.Read(reference!.Value) == secret, "The isolated credential did not round-trip.");
            await service.SaveServerAsync(StorageFixture.Server(), CredentialChange.Forget);
            Assert.Null(credentials.Read(reference.Value));
        }
        finally
        {
            if (reference is Guid value) { credentials.Delete(value); }
            foreach (Guid pending in await fixture.Cleanup.ReadAsync()) { credentials.Delete(pending); }
        }
    }

    private sealed class ObservedLock(IConfigurationLock inner, TaskCompletionSource attempted) : IConfigurationLock
    {
        public ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            attempted.TrySetResult();
            return inner.AcquireAsync(cancellationToken);
        }
    }
}

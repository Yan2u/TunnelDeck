using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Tunnels;
using TunnelDeck.Infrastructure.Persistence;
using TunnelDeck.Infrastructure.Security;
using TunnelDeck.Infrastructure.Ssh;
using TunnelDeck.Infrastructure.Tests.Fixtures;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class DeletionPersistenceTests
{
    [Theory]
    [InlineData(FileFault.PartialWrite)]
    [InlineData(FileFault.Flush)]
    [InlineData(FileFault.Publish)]
    public async Task FailedDeletionKeepsTheExactJsonAndCredentialsAndCanRetry(FileFault fault)
    {
        using var fixture = new StorageFixture();
        AppState initial = StorageFixture.CompleteState();
        fixture.Credentials.Values[StorageFixture.Id(900)] = "disposable-deletion-secret";
        await fixture.Store.CommitAsync(initial, 0);
        var operations = new FaultingFileOperations();
        var configuration = fixture.Service(new JsonStateStore(fixture.Directory.StatePath, null, operations));
        await configuration.InitializeAsync();
        await using var runtime = new DeletionRuntime(fixture, configuration);
        byte[] beforeJson = await File.ReadAllBytesAsync(fixture.Directory.StatePath);
        AppState before = configuration.Current!;
        operations.Fault = fault;
        var result = await runtime.Deletion.DeleteConfirmedAsync(runtime.Deletion.CalcAllDeletionImpact(ResourceKind.Server));
        Assert.Equal(DeletionOutcome.SaveFailed, result.Outcome);
        Assert.Same(before, configuration.Current);
        Assert.Equal(beforeJson, await File.ReadAllBytesAsync(fixture.Directory.StatePath));
        Assert.NotNull(fixture.Credentials.Read(StorageFixture.Id(900)));
        operations.Fault = FileFault.None;
        Assert.True((await runtime.Deletion.DeleteConfirmedAsync(runtime.Deletion.CalcAllDeletionImpact(ResourceKind.Server))).Succeeded);
        Assert.Empty(configuration.Current!.Servers);
        Assert.Equal(2, configuration.Current.Profiles.Length);
        Assert.All(configuration.Current.Profiles, profile => Assert.Empty(profile.Rules));
        Assert.Null(configuration.Current.Settings.DefaultServerId); Assert.Null(configuration.Current.Settings.PinnedQuickServerId);
        Assert.Equal(before.RecentActivity, configuration.Current.RecentActivity);
        Assert.Null(fixture.Credentials.Read(StorageFixture.Id(900)));
    }

    [Fact]
    public async Task AnErrorAfterAtomicPublicationDoesNotReportACommittedDeletionAsFailed()
    {
        using var fixture = new StorageFixture();
        await fixture.Store.CommitAsync(StorageFixture.CompleteState(), 0);
        var operations = new FaultingFileOperations { Fault = FileFault.AfterPublish };
        var configuration = fixture.Service(new JsonStateStore(fixture.Directory.StatePath, null, operations));
        await configuration.InitializeAsync();
        await using var runtime = new DeletionRuntime(fixture, configuration);
        var result = await runtime.Deletion.DeleteConfirmedAsync(runtime.Deletion.CalcAllDeletionImpact(ResourceKind.Server));
        Assert.True(result.Succeeded); Assert.Empty(configuration.Current!.Servers);
        Assert.Empty((await fixture.Store.LoadAsync()).State!.Servers);
    }

    [Fact]
    public async Task SharedCredentialsSurviveUntilTheLastServerAndCleanupFailureIsDurablyRetryable()
    {
        using var fixture = new StorageFixture();
        Guid reference = StorageFixture.Id(900);
        AppState state = StorageFixture.CompleteState();
        state = state with { Servers = [state.Servers[0], state.Servers[1] with { CredentialReference = reference }] };
        fixture.Credentials.Values[reference] = "disposable-shared-secret";
        await fixture.Store.CommitAsync(state, 0);
        var configuration = fixture.Service();
        await configuration.InitializeAsync();
        await using var runtime = new DeletionRuntime(fixture, configuration);
        Assert.True((await runtime.Deletion.DeleteConfirmedAsync(runtime.Deletion.CalcServerDeletionImpact([state.Servers[0].Id]))).Succeeded);
        Assert.NotNull(fixture.Credentials.Read(reference)); Assert.Empty(fixture.Credentials.Deleted);
        fixture.Credentials.OnDelete = _ => throw new IOException("Synthetic credential cleanup failure.");
        var result = await runtime.Deletion.DeleteConfirmedAsync(runtime.Deletion.CalcAllDeletionImpact(ResourceKind.Server));
        Assert.True(result.Succeeded); Assert.Empty(configuration.Current!.Servers);
        Assert.Contains(result.Diagnostics, issue => issue.Path == "Credentials");
        Assert.Contains(reference, await fixture.Cleanup.ReadAsync());
        fixture.Credentials.OnDelete = null;
        Assert.Empty(await configuration.RetryCredentialCleanupAsync());
        Assert.Null(fixture.Credentials.Read(reference)); Assert.Empty(await fixture.Cleanup.ReadAsync());
    }

    private sealed class DeletionRuntime : IAsyncDisposable
    {
        private readonly ConfigurationService _configuration;
        private readonly SshConnectionManager _connections;
        private readonly TunnelManager _tunnels;
        private readonly TunnelActivityRecorder _activity;
        public ResourceDeletionService Deletion { get; }

        public DeletionRuntime(StorageFixture fixture, ConfigurationService configuration)
        {
            _configuration = configuration;
            _connections = new(configuration, new SshSessionFactory(), fixture.Credentials,
                new JsonHostKeyStore(fixture.Directory), new RejectApproval(), networkEnabled: false);
            _tunnels = new(_connections); _activity = new(_tunnels, configuration);
            Deletion = new(configuration, _connections, _tunnels, _activity);
        }

        public async ValueTask DisposeAsync()
        {
            await Deletion.ShutdownAsync(CancellationToken.None);
            await _tunnels.ShutdownAsync(CancellationToken.None);
            await _connections.ShutdownAsync(CancellationToken.None);
            await _activity.ShutdownAsync(CancellationToken.None);
            await _configuration.ShutdownAsync(CancellationToken.None);
        }
    }

    private sealed class RejectApproval : IHostKeyApproval
    {
        public Task<bool> ConfirmAsync(HostKeyApprovalRequest request, CancellationToken cancellationToken) => Task.FromResult(false);
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Validation;
using TunnelDeck.Infrastructure.Persistence;
using TunnelDeck.Infrastructure.Tests.Fixtures;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class JsonStateStoreTests
{
    private static readonly AppStateJsonCodec Codec = new();

    [Fact]
    public async Task EveryFieldRoundTripsThroughDiskWithoutRuntimeOrSecrets()
    {
        using var fixture = new StorageFixture();
        var empty = await fixture.Store.LoadAsync();
        Assert.Equal(0, empty.State!.Revision);
        Assert.False(File.Exists(fixture.Directory.StatePath));
        AppState committed = await fixture.Store.CommitAsync(StorageFixture.CompleteState(), 0);
        AppState restored = (await fixture.Store.LoadAsync()).State!;
        Assert.Equal(1, restored.Revision);
        Assert.Equal(Codec.Encode(committed), Codec.Encode(restored));
        using JsonDocument json = JsonDocument.Parse(await File.ReadAllBytesAsync(fixture.Directory.StatePath));
        Assert.False(json.RootElement.TryGetProperty("activeTunnels", out _));
        Assert.False(json.RootElement.TryGetProperty("trustedHostKeys", out _));
        Assert.False(json.RootElement.GetProperty("servers")[0].TryGetProperty("password", out _));
        Assert.Equal(42, restored.Statistics.TotalForwards);
        Assert.Equal("Deleted server", restored.RecentActivity[0].Source.ServerName);
        Assert.True(AppStateValidator.Validate(restored).IsValid);
    }

    [Theory]
    [InlineData(FileFault.PartialWrite, false)]
    [InlineData(FileFault.Flush, false)]
    [InlineData(FileFault.Publish, false)]
    [InlineData(FileFault.PartialWrite, true)]
    [InlineData(FileFault.Flush, true)]
    [InlineData(FileFault.Publish, true)]
    public async Task FailuresNeverPublishPartialFilesAndCanBeRetried(FileFault fault, bool existing)
    {
        using var fixture = new StorageFixture();
        AppState state = new();
        byte[]? original = null;
        if (existing)
        {
            state = await fixture.Store.CommitAsync(StorageFixture.CompleteState(), 0);
            original = await File.ReadAllBytesAsync(fixture.Directory.StatePath);
        }
        var operations = new FaultingFileOperations { Fault = fault };
        var store = new JsonStateStore(fixture.Directory.StatePath, null, operations);
        AppState candidate = state with { Settings = state.Settings with { SidebarMaximumItems = 7 } };
        var error = await Assert.ThrowsAsync<StateStoreException>(() => store.CommitAsync(candidate, state.Revision));
        Assert.Equal(StateStoreFailure.WriteFailed, error.Failure);
        if (existing) { Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Directory.StatePath)); }
        else { Assert.False(File.Exists(fixture.Directory.StatePath)); }
        Assert.Empty(System.IO.Directory.GetFiles(fixture.Directory.RootPath, "*.tmp"));
        operations.Fault = FileFault.None;
        AppState retried = await store.CommitAsync(candidate, state.Revision);
        Assert.Equal(state.Revision + 1, retried.Revision);
        Assert.Equal(7, (await fixture.Store.LoadAsync()).State!.Settings.SidebarMaximumItems);
        if (existing) { Assert.Equal(original, await File.ReadAllBytesAsync(fixture.Directory.StatePath + ".bak")); }
    }

    [Theory]
    [InlineData(FileFault.AfterPublish)]
    [InlineData(FileFault.Cleanup)]
    public async Task FailuresAfterPublicationStillReportTheCommittedRevision(FileFault fault)
    {
        using var fixture = new StorageFixture();
        var store = new JsonStateStore(fixture.Directory.StatePath, null, new FaultingFileOperations { Fault = fault });
        AppState committed = await store.CommitAsync(new(), 0);
        Assert.Equal(1, committed.Revision);
        Assert.Equal(1, (await fixture.Store.LoadAsync()).State!.Revision);
    }

    [Fact]
    public async Task CancellationAtPublicationDoesNotRollBackOrReportFailure()
    {
        using var fixture = new StorageFixture();
        using var cancellation = new CancellationTokenSource();
        var store = new JsonStateStore(fixture.Directory.StatePath, null, new FaultingFileOperations { AfterPublish = cancellation.Cancel });
        AppState committed = await store.CommitAsync(new(), 0, cancellation.Token);
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(1, committed.Revision);
        Assert.Equal(1, (await fixture.Store.LoadAsync()).State!.Revision);
    }

    [Fact]
    public async Task CancellationBeforePublicationLeavesTheExistingRevision()
    {
        using var fixture = new StorageFixture();
        AppState original = await fixture.Store.CommitAsync(new(), 0);
        using var cancellation = new CancellationTokenSource();
        var store = new JsonStateStore(fixture.Directory.StatePath, null, new FaultingFileOperations
        {
            BeforeFlush = () => { cancellation.Cancel(); return Task.CompletedTask; },
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CommitAsync(original, original.Revision, cancellation.Token));
        Assert.Equal(1, (await fixture.Store.LoadAsync()).State!.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingOrBrokenPrimaryRecoversBackupAndPreservesItOnNextCommit(bool missing)
    {
        using var fixture = new StorageFixture();
        AppState first = await fixture.Store.CommitAsync(StorageFixture.CompleteState(), 0);
        await fixture.Store.CommitAsync(first with { Settings = first.Settings with { SidebarMaximumItems = 3 } }, 1);
        byte[] backup = await File.ReadAllBytesAsync(fixture.Directory.StatePath + ".bak");
        if (missing) { File.Delete(fixture.Directory.StatePath); }
        else { await File.WriteAllTextAsync(fixture.Directory.StatePath, "{broken"); }
        var loaded = await fixture.Store.LoadAsync();
        Assert.True(loaded.RecoveredFromBackup);
        Assert.Equal(1, loaded.State!.Revision);
        Assert.Equal(10, loaded.State.Settings.SidebarMaximumItems);
        await fixture.Store.CommitAsync(loaded.State, loaded.State.Revision);
        Assert.Equal(backup, await File.ReadAllBytesAsync(fixture.Directory.StatePath + ".bak"));
        Assert.False((await fixture.Store.LoadAsync()).RecoveredFromBackup);
    }

    [Fact]
    public async Task BothCorruptFilesProduceAnErrorAndAreNotReset()
    {
        using var fixture = new StorageFixture();
        System.IO.Directory.CreateDirectory(fixture.Directory.RootPath);
        await File.WriteAllTextAsync(fixture.Directory.StatePath, "{broken");
        await File.WriteAllTextAsync(fixture.Directory.StatePath + ".bak", "[]");
        var error = await Assert.ThrowsAsync<StateStoreException>(() => fixture.Store.LoadAsync());
        Assert.Equal(StateStoreFailure.InvalidData, error.Failure);
        await Assert.ThrowsAsync<StateStoreException>(() => fixture.Store.CommitAsync(new(), 0));
        Assert.Equal("{broken", await File.ReadAllTextAsync(fixture.Directory.StatePath));
        Assert.Equal("[]", await File.ReadAllTextAsync(fixture.Directory.StatePath + ".bak"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(AppState.CurrentSchemaVersion + 1)]
    [InlineData(int.MaxValue)]
    public async Task UnsupportedSchemaNeverDowngradesToAnOlderBackup(int version)
    {
        using var fixture = new StorageFixture();
        AppState state = await fixture.Store.CommitAsync(new(), 0);
        await fixture.Store.CommitAsync(state, state.Revision);
        string future = $"{{\"schemaVersion\":{version},\"unfamiliarStructure\":{{\"value\":true}}}}";
        await File.WriteAllTextAsync(fixture.Directory.StatePath, future);
        var result = await fixture.Store.LoadAsync();
        Assert.True(result.IsReadOnly);
        Assert.False(result.RecoveredFromBackup);
        Assert.Null(result.State);
        var error = await Assert.ThrowsAsync<StateStoreException>(() => fixture.Store.CommitAsync(state, 1));
        Assert.Equal(StateStoreFailure.ReadOnly, error.Failure);
        Assert.Equal(future, await File.ReadAllTextAsync(fixture.Directory.StatePath));
    }

    [Fact]
    public async Task RepairsOnlyOrderAndMissingDefaultsWithoutDiscardingHistoricalData()
    {
        using var fixture = new StorageFixture();
        AppState state = StorageFixture.CompleteState();
        AppState broken = state with
        {
            Order = state.Order with { PinnedServerIds = [state.Servers[0].Id, state.Servers[0].Id, StorageFixture.Id(88)], OtherServerIds = [] },
            Settings = state.Settings with { DefaultServerId = StorageFixture.Id(88), PinnedQuickServerId = StorageFixture.Id(89) },
        };
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(broken, VersionedJson.Options);
        System.IO.Directory.CreateDirectory(fixture.Directory.RootPath);
        await File.WriteAllBytesAsync(fixture.Directory.StatePath, bytes);
        var result = await fixture.Store.LoadAsync();
        var repeated = await fixture.Store.LoadAsync();
        Assert.Equal(3, result.Diagnostics.Length);
        Assert.Equal([state.Servers[0].Id], result.State!.Order.PinnedServerIds.ToArray());
        Assert.Equal([state.Servers[1].Id], result.State.Order.OtherServerIds.ToArray());
        Assert.Null(result.State.Settings.DefaultServerId);
        Assert.Null(result.State.Settings.PinnedQuickServerId);
        Assert.Equal(42, result.State.Statistics.TotalForwards);
        Assert.Equal("Deleted server", result.State.RecentActivity[0].Source.ServerName);
        Assert.Equal(Codec.Encode(result.State), Codec.Encode(repeated.State!));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(fixture.Directory.StatePath));
        await fixture.Store.CommitAsync(result.State, 0);
        Assert.Empty((await fixture.Store.LoadAsync()).Diagnostics);
    }

    [Theory]
    [InlineData("duplicate-id")]
    [InlineData("duplicate-rule")]
    [InlineData("bad-port")]
    [InlineData("unknown-auth")]
    [InlineData("unknown-mode")]
    [InlineData("missing-server")]
    [InlineData("remote-auto")]
    [InlineData("negative-count")]
    [InlineData("null-settings")]
    [InlineData("missing-envelope")]
    [InlineData("missing-statistics")]
    [InlineData("missing-counter")]
    [InlineData("missing-rules")]
    [InlineData("missing-mode")]
    [InlineData("missing-port")]
    [InlineData("missing-order")]
    public async Task AmbiguousOrInvalidConfigurationIsNeverSilentlyDropped(string corruption)
    {
        using var fixture = new StorageFixture();
        JsonNode json = JsonNode.Parse(Codec.Encode(StorageFixture.CompleteState()))!;
        switch (corruption)
        {
            case "duplicate-id": json["servers"]![1]!["id"] = StorageFixture.Id(1); break;
            case "duplicate-rule": json["profiles"]![0]!["rules"]![1]!["id"] = StorageFixture.Id(100); break;
            case "bad-port": json["servers"]![0]!["port"] = 65536; break;
            case "unknown-auth": json["servers"]![0]!["authMethod"] = 99; break;
            case "unknown-mode": json["profiles"]![0]!["rules"]![0]!["mode"] = 99; break;
            case "missing-server": json["profiles"]![0]!["rules"]![0]!["serverId"] = StorageFixture.Id(77); break;
            case "remote-auto": json["profiles"]![0]!["rules"]![1]!["autoFindAvailablePort"] = true; break;
            case "negative-count": json["statistics"]!["totalForwards"] = -1; break;
            case "null-settings": json["settings"] = null; break;
            case "missing-envelope": json.AsObject().Remove("servers"); break;
            case "missing-statistics": json.AsObject().Remove("statistics"); break;
            case "missing-counter": json["statistics"]!.AsObject().Remove("serverForwards"); break;
            case "missing-rules": json["profiles"]![0]!.AsObject().Remove("rules"); break;
            case "missing-mode": json["profiles"]![0]!["rules"]![0]!.AsObject().Remove("mode"); break;
            case "missing-port": json["servers"]![0]!.AsObject().Remove("port"); break;
            case "missing-order": json["order"]!.AsObject().Remove("pinnedServerIds"); break;
        }
        string text = json.ToJsonString();
        System.IO.Directory.CreateDirectory(fixture.Directory.RootPath);
        await File.WriteAllTextAsync(fixture.Directory.StatePath, text);
        var error = await Assert.ThrowsAsync<StateStoreException>(() => fixture.Store.LoadAsync());
        Assert.Equal(StateStoreFailure.InvalidData, error.Failure);
        Assert.Equal(text, await File.ReadAllTextAsync(fixture.Directory.StatePath));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{\"schemaVersion\":1,\"servers\":[{\"host\":\"one\",\"host\":\"two\"}]}")]
    public void DuplicateJsonPropertiesAreRejected(string json) =>
        Assert.Equal(StateStoreFailure.InvalidData, Assert.Throws<StateStoreException>(() => Codec.Decode(Encoding.UTF8.GetBytes(json))).Failure);

    [Theory]
    [InlineData("{\"schemaVersion\":2147483648}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":4}")]
    public void FutureSchemaIsProtectedBeforeCheckingTodaysDataShape(string json) =>
        Assert.Equal(StateStoreFailure.UnsupportedSchema, Assert.Throws<StateStoreException>(() => Codec.Decode(Encoding.UTF8.GetBytes(json))).Failure);

    [Fact]
    public async Task DeterministicMigrationCanBeRepeatedAndIsPersistedOnlyByAnEdit()
    {
        using var fixture = new StorageFixture();
        var migration = new FixtureMigration();
        var codec = new AppStateJsonCodec([migration]);
        var store = new JsonStateStore(fixture.Directory.StatePath, codec);
        JsonNode legacy = JsonNode.Parse(Codec.Encode(StorageFixture.CompleteState()))!;
        legacy["schemaVersion"] = 0;
        legacy.AsObject().Remove("settings");
        System.IO.Directory.CreateDirectory(fixture.Directory.RootPath);
        await File.WriteAllTextAsync(fixture.Directory.StatePath, legacy.ToJsonString());
        var first = await store.LoadAsync();
        var second = await store.LoadAsync();
        Assert.Equal(codec.Encode(first.State!), codec.Encode(second.State!));
        Assert.Equal(0, first.State!.Revision);
        Assert.Equal(0, JsonNode.Parse(await File.ReadAllTextAsync(fixture.Directory.StatePath))!["schemaVersion"]!.GetValue<int>());
        await store.CommitAsync(first.State, 0);
        int countAfterSave = migration.Calls;
        Assert.Empty((await store.LoadAsync()).Diagnostics);
        Assert.Equal(countAfterSave, migration.Calls);
        Assert.Equal(1, (await store.LoadAsync()).State!.Revision);
    }

    private sealed class FixtureMigration : IAppStateMigration
    {
        public int FromVersion => 0;
        public int ToVersion => 1;
        public int Calls { get; private set; }
        public JsonObject Migrate(JsonObject source)
        {
            Calls++;
            source["schemaVersion"] = 1;
            source["settings"] = JsonSerializer.SerializeToNode(new AppSettings(), VersionedJson.Options);
            source["settings"]!.AsObject().Remove("defaultAuthenticationMethod");
            source["settings"]!.AsObject().Remove("defaultIdentityFilePath");
            source["settings"]!.AsObject().Remove("language");
            return source;
        }
    }
}

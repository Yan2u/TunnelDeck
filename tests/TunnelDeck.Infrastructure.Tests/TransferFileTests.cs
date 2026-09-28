using System.Text;
using System.Text.Json.Nodes;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Transfer;
using TunnelDeck.Infrastructure.Persistence;
using TunnelDeck.Infrastructure.Tests.Fixtures;
using TunnelDeck.Infrastructure.Transfer;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class TransferFileTests
{
    [Fact]
    public void PortableJsonContainsOnlyResourcesOrderAndPathHints()
    {
        TunnelDeckExport export = ExportBuilder.Build(StorageFixture.CompleteState(), ExportScope.Everything, StorageFixture.Now);
        string json = Encoding.UTF8.GetString(ExportJsonCodec.Encode(export));
        foreach (string excluded in new[] { "credentialReference", "password", "passphrase", "lastUsedAt", "statistics", "recentActivity", "trusted", "activeTunnels", "settings" })
        { Assert.DoesNotContain(excluded, json, StringComparison.OrdinalIgnoreCase); }
        Assert.DoesNotContain(StorageFixture.Id(900).ToString(), json);
        Assert.Contains("identityFilePath", json);
        TunnelDeckExport decoded = ExportJsonCodec.Decode(Encoding.UTF8.GetBytes(json));
        Assert.Equal(export.Servers.ToArray(), decoded.Servers.ToArray());
        Assert.Equal(export.Order.PinnedServerIds.ToArray(), decoded.Order.PinnedServerIds.ToArray());
        Assert.Equal(export.Profiles[0].Rules.ToArray(), decoded.Profiles[0].Rules.ToArray());
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("corrupt")]
    [InlineData("duplicate-property")]
    [InlineData("credential")]
    [InlineData("trust")]
    [InlineData("missing-property")]
    [InlineData("null-array")]
    [InlineData("null-entry")]
    [InlineData("string-port")]
    public void InvalidJsonIsRejectedBeforeAPlanCanBeCommitted(string fault)
    {
        JsonObject root = JsonNode.Parse(ExportJsonCodec.Encode(Export()))!.AsObject();
        string json;
        switch (fault)
        {
            case "schema": root["schemaVersion"] = 2; break;
            case "credential": root["servers"]![0]!["credentialReference"] = Guid.NewGuid().ToString(); break;
            case "trust": root["trustedHostKeys"] = new JsonArray(); break;
            case "missing-property": root["servers"]![0]!.AsObject().Remove("username"); break;
            case "null-array": root["servers"] = null; break;
            case "null-entry": root["servers"]![0] = null; break;
            case "string-port": root["servers"]![0]!["port"] = "22"; break;
        }
        json = fault switch
        {
            "corrupt" => "{broken",
            "duplicate-property" => root.ToJsonString().Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1"),
            _ => root.ToJsonString(),
        };
        Assert.Throws<ImportDocumentException>(() => ExportJsonCodec.Decode(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public async Task Utf8BomIsAcceptedAndOversizedOrMissingFilesLeaveConfigurationUntouched()
    {
        using var fixture = new StorageFixture();
        Directory.CreateDirectory(fixture.Directory.RootPath);
        string path = Path.Combine(fixture.Directory.RootPath, "export.json");
        byte[] json = ExportJsonCodec.Encode(Export());
        await File.WriteAllBytesAsync(path, [0xef, 0xbb, 0xbf, .. json]);
        var files = new ResourceTransferFiles();
        Assert.True((await files.PreviewAsync(path, ImportSource.TunnelDeckJson, new())).CanCommit);
        await using (var stream = new FileStream(path, FileMode.Open, FileAccess.Write)) { stream.SetLength(ExportJsonCodec.MaximumBytes + 1); }
        await Assert.ThrowsAsync<ImportDocumentException>(() => files.PreviewAsync(path, ImportSource.TunnelDeckJson, new()));
        await Assert.ThrowsAsync<ImportDocumentException>(() => files.PreviewAsync(path + ".missing", ImportSource.TunnelDeckJson, new()));
        Assert.False(File.Exists(fixture.Directory.StatePath));
    }

    [Theory]
    [InlineData(FileFault.PartialWrite)]
    [InlineData(FileFault.Flush)]
    [InlineData(FileFault.Publish)]
    public async Task FailedImportKeepsTheWholeOldSnapshotAndCanRetry(FileFault fault)
    {
        using var fixture = new StorageFixture();
        await fixture.Store.CommitAsync(new(), 0);
        byte[] before = await File.ReadAllBytesAsync(fixture.Directory.StatePath);
        var operations = new FaultingFileOperations { Fault = fault };
        ConfigurationService configuration = fixture.Service(new JsonStateStore(fixture.Directory.StatePath, null, operations));
        await configuration.InitializeAsync();
        AppState state = configuration.Current!;
        var importer = new ResourceImportService(configuration, new ServerWorkRegistry(), File.Exists);
        ImportPlan plan = ImportPlanner.Plan(Export(), state, File.Exists);
        await Assert.ThrowsAsync<StateStoreException>(() => importer.CommitAsync(plan));
        Assert.Same(state, configuration.Current);
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Directory.StatePath));
        operations.Fault = FileFault.None;
        await importer.CommitAsync(plan);
        AppState reloaded = (await fixture.Store.LoadAsync()).State!;
        Assert.Single(reloaded.Servers);
        Assert.Single(reloaded.Profiles);
        Assert.Equal(reloaded.Servers[0].Id, reloaded.Profiles[0].Rules[0].ServerId);
        Assert.Equal(state.Revision + 1, reloaded.Revision);
        Assert.Empty(fixture.Credentials.Values);
    }

    [Fact]
    public async Task FailureReportedAfterAtomicPublishIsReconciledAsOneSuccessfulImport()
    {
        using var fixture = new StorageFixture();
        var operations = new FaultingFileOperations { Fault = FileFault.AfterPublish };
        ConfigurationService configuration = fixture.Service(new JsonStateStore(fixture.Directory.StatePath, null, operations));
        await configuration.InitializeAsync();
        var importer = new ResourceImportService(configuration, new ServerWorkRegistry(), File.Exists);
        await importer.CommitAsync(ImportPlanner.Plan(Export(), configuration.Current!, File.Exists));
        Assert.Equal(1, configuration.Current!.Revision);
        Assert.Single((await fixture.Store.LoadAsync()).State!.Servers);
    }

    [Fact]
    public async Task ExportIsAtomicAndCannotOverwriteSshConfigOrLiveData()
    {
        using var fixture = new StorageFixture();
        Directory.CreateDirectory(fixture.Directory.RootPath);
        string exportPath = Path.Combine(fixture.Directory.RootPath, "backup.json");
        var files = new ResourceTransferFiles();
        await files.ExportAsync(exportPath, Export());
        byte[] original = await File.ReadAllBytesAsync(exportPath);
        using (FileStream locked = File.Open(exportPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        { await Assert.ThrowsAsync<ImportDocumentException>(() => files.ExportAsync(exportPath, new())); }
        Assert.Equal(original, await File.ReadAllBytesAsync(exportPath));
        Assert.Empty(Directory.GetFiles(fixture.Directory.RootPath, "*.tmp"));
        await Assert.ThrowsAsync<ImportDocumentException>(() => files.ExportAsync(Path.Combine(fixture.Directory.RootPath, "config"), Export()));
        await Assert.ThrowsAsync<ImportDocumentException>(() => new ResourceTransferFiles(fixture.Directory.RootPath).ExportAsync(fixture.Directory.StatePath, Export()));
        Assert.False(File.Exists(fixture.Directory.StatePath));
    }

    [Fact]
    public async Task CancellationBeforeExportNeverTruncatesExistingFile()
    {
        using var fixture = new StorageFixture();
        Directory.CreateDirectory(fixture.Directory.RootPath);
        string path = Path.Combine(fixture.Directory.RootPath, "backup.json");
        await File.WriteAllTextAsync(path, "keep");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ResourceTransferFiles().ExportAsync(path, Export(), cancelled.Token));
        Assert.Equal("keep", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task ClosingCancelsAcceptedFileWorkAndRejectsNewWorkWithoutReplacingTheDestination()
    {
        using var fixture = new StorageFixture();
        Directory.CreateDirectory(fixture.Directory.RootPath);
        string path = Path.Combine(fixture.Directory.RootPath, "backup.json");
        await File.WriteAllTextAsync(path, "previous file");
        using IDisposable held = await new FileConfigurationLock(path + ".lock").AcquireAsync();
        var files = new ResourceTransferFiles();
        Task exporting = files.ExportAsync(path, Export());
        await files.ShutdownAsync(CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exporting);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => files.ExportAsync(path, Export()));
        Assert.Equal("previous file", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(fixture.Directory.RootPath, "*.tmp"));
    }

    private static TunnelDeckExport Export()
    {
        RemoteServer server = StorageFixture.Server();
        var profile = new ForwardProfile { Id = Guid.NewGuid(), Name = "Profile", Rules =
            [new() { Id = Guid.NewGuid(), ServerId = server.Id, LocalPort = 3000, RemotePort = 5432, AutoFindAvailablePort = true }] };
        return ExportBuilder.Build(new() { Servers = [server], Profiles = [profile],
            Order = new() { OtherServerIds = [server.Id], OtherProfileIds = [profile.Id] } }, ExportScope.Everything, StorageFixture.Now);
    }
}

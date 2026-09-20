using System.Text.Json.Nodes;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Security;
using TunnelDeck.Infrastructure.Persistence;
using TunnelDeck.Infrastructure.Security;
using TunnelDeck.Infrastructure.Tests.Fixtures;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class HostKeyPersistenceTests
{
    [Fact]
    public async Task ConcurrentFirstUseApprovalsCannotOverwriteEachOthersKeys()
    {
        using var fixture = new StorageFixture();
        var first = new JsonHostKeyStore(fixture.Directory);
        var second = new JsonHostKeyStore(fixture.Directory);
        var key1 = new TrustedHostKey(HostKeyIdentity.FromKey("host.example", 22, "ssh-ed25519", [1]), StorageFixture.Now);
        var key2 = key1 with { Identity = HostKeyIdentity.FromKey("host.example", 22, "ssh-ed25519", [2]) };
        Exception?[] results = await Task.WhenAll(
            Record.ExceptionAsync(() => first.TrustNewAsync(key1)),
            Record.ExceptionAsync(() => second.TrustNewAsync(key2)));
        Assert.Single(results, result => result is null);
        Assert.Equal(ConnectionFailure.HostKeyChanged, Assert.IsType<ConnectionException>(Assert.Single(results, result => result is not null)).Failure);
        TrustedHostKey stored = (await first.FindAsync("host.example", 22, "ssh-ed25519"))!;
        Assert.True(stored.Identity.Matches(results[0] is null ? key1.Identity : key2.Identity));
    }

    [Fact]
    public async Task ApprovingTheSameKeyAgainPreservesTheOriginalTrustRecord()
    {
        using var fixture = new StorageFixture();
        var store = new JsonHostKeyStore(fixture.Directory);
        var key = new TrustedHostKey(HostKeyIdentity.FromKey("host.example", 22, "ssh-ed25519", [1]), StorageFixture.Now);
        await store.TrustNewAsync(key);
        byte[] before = await File.ReadAllBytesAsync(fixture.Directory.HostKeysPath);
        await store.TrustNewAsync(key with { TrustedAt = StorageFixture.Now.AddDays(1) });
        Assert.Equal(before, await File.ReadAllBytesAsync(fixture.Directory.HostKeysPath));
    }

    [Fact]
    public async Task TrustRoundTripsSeparatelyAndUsesHostPortAndAlgorithmIdentity()
    {
        using var fixture = new StorageFixture();
        var store = new JsonHostKeyStore(fixture.Directory);
        var key = new TrustedHostKey(HostKeyIdentity.FromKey("BÜCHER.example", 2222, "ssh-ed25519", [1, 2, 3]), StorageFixture.Now);
        await store.TrustAsync(key);
        var reopened = new JsonHostKeyStore(fixture.Directory);
        TrustedHostKey? found = await reopened.FindAsync("xn--bcher-kva.example", 2222, "ssh-ed25519");
        Assert.NotNull(found);
        Assert.True(found.Identity.Matches(key.Identity));
        Assert.Null(await reopened.FindAsync("xn--bcher-kva.example", 22, "ssh-ed25519"));
        Assert.Null(await reopened.FindAsync("xn--bcher-kva.example", 2222, "rsa-sha2-256"));
        var changed = key with { Identity = HostKeyIdentity.FromKey("xn--bcher-kva.example", 2222, "ssh-ed25519", [9, 8, 7]) };
        Assert.False(found.Identity.Matches(changed.Identity));
        await reopened.TrustAsync(changed);
        Assert.True((await store.FindAsync("BÜCHER.example", 2222, "ssh-ed25519"))!.Identity.Matches(changed.Identity));
        Assert.False(File.Exists(fixture.Directory.StatePath));
    }

    [Fact]
    public async Task SavingServerConfigurationNeverGrantsHostTrust()
    {
        using var fixture = new StorageFixture();
        var service = fixture.Service();
        await service.InitializeAsync();
        RemoteServer server = StorageFixture.Server();
        await service.SaveServerAsync(server, CredentialChange.Forget);
        var store = new JsonHostKeyStore(fixture.Directory);
        Assert.Null(await store.FindAsync(server.Host, server.Port, "ssh-ed25519"));
        Assert.False(File.Exists(fixture.Directory.HostKeysPath));
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("future")]
    [InlineData("missing")]
    public async Task InvalidTrustNeverSilentlyRestoresOlderTrustedKeys(string fault)
    {
        using var fixture = new StorageFixture();
        var store = new JsonHostKeyStore(fixture.Directory);
        var first = new TrustedHostKey(HostKeyIdentity.FromKey("host.example", 22, "ssh-ed25519", [1]), StorageFixture.Now);
        await store.TrustAsync(first);
        var changed = first with { Identity = HostKeyIdentity.FromKey("host.example", 22, "ssh-ed25519", [2]) };
        await store.TrustAsync(changed);
        byte[] backup = await File.ReadAllBytesAsync(fixture.Directory.HostKeysPath + ".bak");
        if (fault == "missing") { File.Delete(fixture.Directory.HostKeysPath); }
        else { await File.WriteAllTextAsync(fixture.Directory.HostKeysPath, fault == "future" ? "{\"schemaVersion\":2}" : "{}"); }
        await Assert.ThrowsAsync<StateStoreException>(() => store.FindAsync("host.example", 22, "ssh-ed25519"));
        await Assert.ThrowsAsync<StateStoreException>(() => store.TrustAsync(first));
        Assert.Equal(backup, await File.ReadAllBytesAsync(fixture.Directory.HostKeysPath + ".bak"));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("bad-fingerprint")]
    [InlineData("bad-port")]
    [InlineData("null-key")]
    public async Task MalformedTrustEntriesFailClosed(string fault)
    {
        using var fixture = new StorageFixture();
        var store = new JsonHostKeyStore(fixture.Directory);
        await store.TrustAsync(new(HostKeyIdentity.FromKey("host.example", 22, "ssh-ed25519", [1]), StorageFixture.Now));
        JsonNode document = JsonNode.Parse(await File.ReadAllTextAsync(fixture.Directory.HostKeysPath))!;
        switch (fault)
        {
            case "duplicate": document["keys"]!.AsArray().Add(document["keys"]![0]!.DeepClone()); break;
            case "bad-fingerprint": document["keys"]![0]!["identity"]!["fingerprint"] = "SHA256:invalid"; break;
            case "bad-port": document["keys"]![0]!["identity"]!["port"] = 0; break;
            case "null-key": document["keys"]![0] = null; break;
        }
        await File.WriteAllTextAsync(fixture.Directory.HostKeysPath, document.ToJsonString());
        await Assert.ThrowsAsync<StateStoreException>(() => store.FindAsync("host.example", 22, "ssh-ed25519"));
    }

    [Fact]
    public async Task CleanupJournalNeverFallsBackToAPotentiallyStaleDeletionList()
    {
        using var fixture = new StorageFixture();
        Guid reference = Guid.NewGuid();
        await fixture.Cleanup.AddAsync([reference]);
        await fixture.Cleanup.RemoveAsync(reference);
        await File.WriteAllTextAsync(fixture.Directory.CredentialCleanupPath, "{}");
        await Assert.ThrowsAsync<StateStoreException>(() => fixture.Cleanup.ReadAsync());
        await Assert.ThrowsAsync<StateStoreException>(() => fixture.Cleanup.AddAsync([Guid.NewGuid()]));
    }

    [Fact]
    public void UnpackagedDataDefaultsToLocalAppDataAndProbeDataIsIsolated()
    {
        var normal = AppDataDirectory.ForCurrentUser();
        Assert.Equal(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TunnelDeck"), normal.RootPath);
        var probe = AppDataDirectory.ForWindowProbe();
        Assert.NotEqual(normal.RootPath, probe.RootPath);
        Assert.NotEqual(probe.RootPath, AppDataDirectory.ForWindowProbe().RootPath);
        Assert.Throws<ArgumentException>(() => new AppDataDirectory("relative-data"));
        Assert.Throws<ArgumentException>(() => new AppDataDirectory(AppContext.BaseDirectory));
        Assert.Throws<ArgumentException>(() => new AppDataDirectory(Path.Combine(AppContext.BaseDirectory, "data")));
    }
}

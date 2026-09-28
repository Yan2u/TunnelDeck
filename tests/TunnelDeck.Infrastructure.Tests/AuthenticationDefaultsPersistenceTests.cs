using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Policies;
using TunnelDeck.Infrastructure.Persistence;
using TunnelDeck.Infrastructure.Tests.Fixtures;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class AuthenticationDefaultsPersistenceTests
{
    [Fact]
    public async Task SchemaOneLoadsWithPasswordDefaultsAndMigratesOnlyAfterAnActualEdit()
    {
        using var fixture = new StorageFixture();
        byte[] legacy = LegacyState();
        Directory.CreateDirectory(fixture.Directory.RootPath);
        await File.WriteAllBytesAsync(fixture.Directory.StatePath, legacy);
        var service = fixture.Service();
        var loaded = await service.InitializeAsync();
        Assert.Equal(AppState.CurrentSchemaVersion, loaded.State!.SchemaVersion);
        Assert.Equal(AuthenticationMethod.Password, loaded.State.Settings.DefaultAuthenticationMethod);
        Assert.Null(loaded.State.Settings.DefaultIdentityFilePath);
        Assert.Equal(42, loaded.State.Statistics.TotalForwards);
        Assert.Contains(loaded.Diagnostics, issue => issue.Message.Contains("schema 1 to 2"));
        Assert.Equal(legacy, await File.ReadAllBytesAsync(fixture.Directory.StatePath));
        await service.UpdateSettingsAsync(settings => settings with { SidebarMaximumItems = settings.SidebarMaximumItems });
        Assert.Equal(legacy, await File.ReadAllBytesAsync(fixture.Directory.StatePath));
        await service.UpdateSettingsAsync(settings => settings with { DefaultAuthenticationMethod = AuthenticationMethod.PrivateKey });
        byte[] current = await File.ReadAllBytesAsync(fixture.Directory.StatePath);
        Assert.Equal(legacy, await File.ReadAllBytesAsync(fixture.Directory.StatePath + ".bak"));
        using JsonDocument document = JsonDocument.Parse(current);
        Assert.Equal(AppState.CurrentSchemaVersion, document.RootElement.GetProperty("schemaVersion").GetInt32());
        // An older reader sees an unsupported schema before interpreting or replacing any data.
        Assert.Equal(StateStoreFailure.UnsupportedSchema, Assert.Throws<StateStoreException>(() => VersionedJson.ReadSchema(document.RootElement, 1)).Failure);
        var reloaded = await fixture.Store.LoadAsync();
        Assert.Empty(reloaded.Diagnostics);
        Assert.Equal(AuthenticationMethod.PrivateKey, reloaded.State!.Settings.DefaultAuthenticationMethod);
        Assert.Equal(loaded.State.Servers.ToArray(), reloaded.State.Servers.ToArray());
        Assert.Equal(loaded.State.Statistics.TotalForwards, reloaded.State.Statistics.TotalForwards);
    }

    [Theory]
    [InlineData("missing-old-setting")]
    [InlineData("unknown-old-setting")]
    public async Task MigrationDoesNotTurnInvalidSchemaOneSettingsIntoValidDefaults(string fault)
    {
        using var fixture = new StorageFixture();
        JsonObject document = JsonNode.Parse(LegacyState())!.AsObject();
        JsonObject settings = document["settings"]!.AsObject();
        if (fault == "missing-old-setting") { settings.Remove("autoFindAvailablePortDefault"); }
        else { settings["unrecognizedSetting"] = true; }
        Directory.CreateDirectory(fixture.Directory.RootPath);
        string text = document.ToJsonString();
        await File.WriteAllTextAsync(fixture.Directory.StatePath, text);
        await Assert.ThrowsAsync<StateStoreException>(() => fixture.Store.LoadAsync());
        Assert.Equal(text, await File.ReadAllTextAsync(fixture.Directory.StatePath));
    }

    [Theory]
    [InlineData("defaultAuthenticationMethod")]
    [InlineData("defaultIdentityFilePath")]
    public void SchemaTwoRequiresItsAuthenticationSettings(string field)
    {
        var codec = new AppStateJsonCodec();
        JsonObject document = JsonNode.Parse(codec.Encode(new()))!.AsObject();
        document["schemaVersion"] = 2;
        document["settings"]!.AsObject().Remove("language");
        document["settings"]!.AsObject().Remove(field);
        Assert.Throws<StateStoreException>(() => codec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString())));
    }

    [Fact]
    public async Task ExplicitDefaultsRoundTripAndPrefillOnlyNewServers()
    {
        using var fixture = new StorageFixture();
        var service = fixture.Service();
        await service.InitializeAsync();
        var original = StorageFixture.Server();
        await service.SaveServerAsync(original, CredentialChange.Forget);
        string key = Path.Combine(fixture.Directory.RootPath, "default key");
        await service.UpdateSettingsAsync(settings => settings with
        { DefaultAuthenticationMethod = AuthenticationMethod.PrivateKey, DefaultIdentityFilePath = key });
        var reopened = fixture.Service();
        await reopened.InitializeAsync();
        AppSettings settings = reopened.Current!.Settings;
        Assert.Equal(key, settings.DefaultIdentityFilePath);
        Assert.Equal(AuthenticationMethod.PrivateKey, settings.DefaultAuthenticationMethod);
        var draft = ServerDefaultsResolver.CreateDraft(settings, fixture.Directory.RootPath, _ => false);
        Assert.Equal(key, draft.IdentityFilePath);
        Assert.Equal(AuthenticationMethod.PrivateKey, draft.AuthMethod);
        Assert.Equal(AuthenticationMethod.Password, reopened.Current.Servers[0].AuthMethod);
        Assert.Null(reopened.Current.Servers[0].IdentityFilePath);
        await reopened.UpdateSettingsAsync(value => value with { DefaultAuthenticationMethod = AuthenticationMethod.Password });
        Assert.Equal(key, reopened.Current.Settings.DefaultIdentityFilePath);
        Assert.Null(ServerDefaultsResolver.CreateDraft(reopened.Current.Settings, fixture.Directory.RootPath, _ => false).IdentityFilePath);
    }

    private static byte[] LegacyState()
    {
        var codec = new AppStateJsonCodec();
        JsonObject document = JsonNode.Parse(codec.Encode(StorageFixture.CompleteState()))!.AsObject();
        document["schemaVersion"] = 1;
        document["settings"]!.AsObject().Remove("defaultAuthenticationMethod");
        document["settings"]!.AsObject().Remove("defaultIdentityFilePath");
        document["settings"]!.AsObject().Remove("language");
        return Encoding.UTF8.GetBytes(document.ToJsonString());
    }
}

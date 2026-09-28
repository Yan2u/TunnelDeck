using System.Text;
using System.Text.Json.Nodes;
using TunnelDeck.Core.Localization;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Infrastructure.Persistence;
using TunnelDeck.Infrastructure.Tests.Fixtures;

namespace TunnelDeck.Infrastructure.Tests;

public sealed class LanguagePersistenceTests
{
    private static readonly AppStateJsonCodec Codec = new();

    [Fact]
    public async Task SchemaTwoIsMigratedInMemoryAndLanguageEditsPreserveAllOtherData()
    {
        using var fixture = new StorageFixture();
        AppState original = StorageFixture.CompleteState() with
        {
            Settings = StorageFixture.CompleteState().Settings with
            { DefaultAuthenticationMethod = AuthenticationMethod.PrivateKey, DefaultIdentityFilePath = @"C:\fixture\id_rsa" },
        };
        byte[] legacy = SchemaTwo(original);
        Directory.CreateDirectory(fixture.Directory.RootPath);
        await File.WriteAllBytesAsync(fixture.Directory.StatePath, legacy);
        var service = fixture.Service();
        var loaded = await service.InitializeAsync();
        Assert.Equal(AppState.CurrentSchemaVersion, loaded.State!.SchemaVersion);
        Assert.Equal(UiText.English, loaded.State.Settings.Language);
        Assert.Equal(Codec.Encode(original), Codec.Encode(loaded.State));
        Assert.Contains(loaded.Diagnostics, issue => issue.Message.Contains("schema 2 to 3"));
        await service.UpdateSettingsAsync(settings => settings with { Language = UiText.English });
        Assert.Equal(legacy, await File.ReadAllBytesAsync(fixture.Directory.StatePath));

        var saved = await service.UpdateSettingsAsync(settings => settings with { Language = UiText.SimplifiedChinese });
        Assert.Equal(legacy, await File.ReadAllBytesAsync(fixture.Directory.StatePath + ".bak"));
        var reopened = await fixture.Store.LoadAsync();
        Assert.Empty(reopened.Diagnostics);
        Assert.Equal(UiText.SimplifiedChinese, reopened.State!.Settings.Language);
        Assert.Equal(Codec.Encode(original with
        {
            Revision = saved.State.Revision,
            Settings = original.Settings with { Language = UiText.SimplifiedChinese },
        }), Codec.Encode(reopened.State));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    public void SchemaTwoMigrationRetainsStrictSettingsValidation(string fault)
    {
        JsonObject document = JsonNode.Parse(SchemaTwo(new()))!.AsObject();
        if (fault == "missing") { document["settings"]!.AsObject().Remove("autoFindAvailablePortDefault"); }
        else { document["settings"]!["unexpectedSetting"] = true; }
        Assert.Equal(StateStoreFailure.InvalidData, Assert.Throws<StateStoreException>(() =>
            Codec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString()))).Failure);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("unsupported")]
    public void CurrentSchemaRequiresASupportedLanguage(string fault)
    {
        JsonObject document = JsonNode.Parse(Codec.Encode(new()))!.AsObject();
        if (fault == "missing") { document["settings"]!.AsObject().Remove("language"); }
        else { document["settings"]!["language"] = fault == "null" ? null : "zh-TW"; }
        Assert.Equal(StateStoreFailure.InvalidData, Assert.Throws<StateStoreException>(() =>
            Codec.Decode(Encoding.UTF8.GetBytes(document.ToJsonString()))).Failure);
    }

    private static byte[] SchemaTwo(AppState state)
    {
        JsonObject document = JsonNode.Parse(Codec.Encode(state))!.AsObject();
        document["schemaVersion"] = 2;
        document["settings"]!.AsObject().Remove("language");
        return Encoding.UTF8.GetBytes(document.ToJsonString());
    }
}

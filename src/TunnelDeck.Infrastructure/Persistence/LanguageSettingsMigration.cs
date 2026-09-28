using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Infrastructure.Persistence;

/// <summary>Schema 3 saves an explicit UI language. Existing installations keep English.</summary>
internal sealed class LanguageSettingsMigration : IAppStateMigration
{
    public int FromVersion => 2;
    public int ToVersion => 3;

    public JsonObject Migrate(JsonObject source)
    {
        if (source["settings"] is not JsonObject settings) { throw VersionedJson.Invalid(); }
        try { _ = settings.Deserialize<SettingsV2>(VersionedJson.Options) ?? throw VersionedJson.Invalid(); }
        catch (JsonException) { throw VersionedJson.Invalid(); }
        settings["language"] = UiText.English;
        source["schemaVersion"] = ToVersion;
        return source;
    }

    private sealed record SettingsV2
    {
        [JsonRequired] public int SidebarMaximumItems { get; init; }
        [JsonRequired] public Guid? DefaultServerId { get; init; }
        [JsonRequired] public Guid? PinnedQuickServerId { get; init; }
        [JsonRequired] public bool AutoFindAvailablePortDefault { get; init; }
        [JsonRequired] public AuthenticationMethod DefaultAuthenticationMethod { get; init; }
        [JsonRequired] public string? DefaultIdentityFilePath { get; init; }
    }
}

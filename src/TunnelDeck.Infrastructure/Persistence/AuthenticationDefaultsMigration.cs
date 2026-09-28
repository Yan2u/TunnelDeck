using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace TunnelDeck.Infrastructure.Persistence;

/// <summary>Schema 2 adds authentication defaults; schema 1 retains its password default and all existing resources.</summary>
internal sealed class AuthenticationDefaultsMigration : IAppStateMigration
{
    public int FromVersion => 1;
    public int ToVersion => 2;

    public JsonObject Migrate(JsonObject source)
    {
        if (source["settings"] is not JsonObject settings) { throw VersionedJson.Invalid(); }
        // Keep schema 1's required fields and unknown-field protection before adding the documented defaults.
        try { _ = settings.Deserialize<SettingsV1>(VersionedJson.Options) ?? throw VersionedJson.Invalid(); }
        catch (JsonException) { throw VersionedJson.Invalid(); }
        settings["defaultAuthenticationMethod"] = 0;
        settings["defaultIdentityFilePath"] = null;
        source["schemaVersion"] = ToVersion;
        return source;
    }

    private sealed record SettingsV1
    {
        [JsonRequired] public int SidebarMaximumItems { get; init; }
        [JsonRequired] public Guid? DefaultServerId { get; init; }
        [JsonRequired] public Guid? PinnedQuickServerId { get; init; }
        [JsonRequired] public bool AutoFindAvailablePortDefault { get; init; }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using TunnelDeck.Core.Persistence;

namespace TunnelDeck.Infrastructure.Persistence;

internal static class VersionedJson
{
    internal static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    public static JsonDocument Parse(byte[] bytes)
    {
        try { return JsonDocument.Parse(bytes); }
        catch (JsonException) { throw Invalid(); }
    }

    public static int ReadSchema(JsonElement root, int currentVersion)
    {
        if (root.ValueKind != JsonValueKind.Object) { throw Invalid(); }
        int? version = null;
        int count = 0;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Name != "schemaVersion") { continue; }
            count++;
            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out long candidate))
            {
                // Inspect the version before applying today's shape/duplicate-property rules.
                if (candidate > currentVersion) { throw Unsupported(); }
                if (candidate >= 0) { version = (int)candidate; }
            }
        }
        if (count != 1 || version is null || version < 0) { throw Invalid(); }
        RejectDuplicateProperties(root);
        return version.Value;
    }

    public static T Deserialize<T>(JsonElement root)
    {
        try { return root.Deserialize<T>(Options) ?? throw Invalid(); }
        catch (JsonException) { throw Invalid(); }
    }

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static StateStoreException Invalid() => new(StateStoreFailure.InvalidData,
        "A saved data file is invalid. Restore a valid file or backup and retry.");

    public static StateStoreException Unsupported() => new(StateStoreFailure.UnsupportedSchema,
        "This saved data format is not supported by this version of TunnelDeck. The file must not be overwritten.");

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) { throw Invalid(); }
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray()) { RejectDuplicateProperties(child); }
        }
    }
}

using System.Text.Json;
using System.Text.Json.Serialization;
using TunnelDeck.Core.Transfer;

namespace TunnelDeck.Infrastructure.Transfer;

public static class ExportJsonCodec
{
    public const int MaximumBytes = 8 * 1024 * 1024;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false, NumberHandling = JsonNumberHandling.Strict,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow, WriteIndented = true, MaxDepth = 32,
    };

    public static TunnelDeckExport Decode(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.Length > MaximumBytes) { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ExportJsonCodec_ChooseAnExportSmallerThan8MiB")); }
        if (bytes.Span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF })) { bytes = bytes[3..]; }
        try
        {
            using JsonDocument json = JsonDocument.Parse(bytes, new() { MaxDepth = 32 });
            JsonElement root = json.RootElement;
            RejectDuplicateProperties(root);
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out JsonElement version) ||
                !version.TryGetInt32(out int schema) || schema != TunnelDeckExport.CurrentSchemaVersion)
            { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ExportJsonCodec_ThisExportVersionIsMissingOrUnsupportedUse")); }
            TunnelDeckExport export = root.Deserialize<TunnelDeckExport>(Options) ??
                throw new ImportDocumentException(UiText.Invariant("Diagnostic_ExportJsonCodec_TheExportIsEmpty"));
            ImportPlanner.Validate(export);
            return export;
        }
        catch (JsonException)
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ExportJsonCodec_TheExportIsNotValidJSONForThis")); }
        catch (InvalidOperationException)
        { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ExportJsonCodec_TheExportHasAnInvalidFieldTypeNothing")); }
    }

    public static byte[] Encode(TunnelDeckExport export)
    {
        ImportPlanner.Validate(export);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(export, Options);
        if (bytes.Length > MaximumBytes) { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ExportJsonCodec_ExportFewerResourcesAtATimeThisFile")); }
        return bytes;
    }

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) { throw new ImportDocumentException(UiText.Invariant("Diagnostic_ExportJsonCodec_TheExportRepeatsAJSONPropertyRemoveThe")); }
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        { foreach (JsonElement child in element.EnumerateArray()) { RejectDuplicateProperties(child); } }
    }
}

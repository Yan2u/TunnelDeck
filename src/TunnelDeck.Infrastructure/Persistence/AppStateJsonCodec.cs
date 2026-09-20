using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Infrastructure.Persistence;

public sealed class AppStateJsonCodec
{
    private readonly IReadOnlyDictionary<int, IAppStateMigration> _migrations;

    // Schema 1 is the first persisted format. No speculative schema-0 migration is installed in production.
    public AppStateJsonCodec(IEnumerable<IAppStateMigration>? migrations = null)
    {
        _migrations = (migrations ?? []).ToDictionary(migration => migration.FromVersion);
        foreach (IAppStateMigration migration in _migrations.Values)
        {
            if (migration.FromVersion < 0 || migration.ToVersion <= migration.FromVersion ||
                migration.ToVersion > AppState.CurrentSchemaVersion)
            {
                throw new ArgumentException("Migrations must advance to a supported schema.", nameof(migrations));
            }
        }
    }

    public byte[] Encode(AppState state)
    {
        AppStateValidator.Validate(state).ThrowIfInvalid();
        return VersionedJson.Serialize(state);
    }

    public StateLoadResult Decode(byte[] bytes)
    {
        using JsonDocument document = VersionedJson.Parse(bytes);
        int version = VersionedJson.ReadSchema(document.RootElement, AppState.CurrentSchemaVersion);
        var diagnostics = ImmutableArray.CreateBuilder<ValidationIssue>();
        JsonElement root = document.RootElement;
        while (version < AppState.CurrentSchemaVersion)
        {
            if (!_migrations.TryGetValue(version, out IAppStateMigration? migration)) { throw VersionedJson.Unsupported(); }
            var source = JsonNode.Parse(root.GetRawText())!.AsObject();
            JsonObject migrated = migration.Migrate(source);
            root = JsonSerializer.SerializeToElement(migrated, VersionedJson.Options);
            if (VersionedJson.ReadSchema(root, AppState.CurrentSchemaVersion) != migration.ToVersion) { throw VersionedJson.Invalid(); }
            diagnostics.Add(new("SchemaVersion", $"Migrated schema {version} to {migration.ToVersion} in memory; the original is retained until a successful edit."));
            version = migration.ToVersion;
        }
        StateLoadResult result = StateLoadRepair.Apply(VersionedJson.Deserialize<AppState>(root));
        return result with { Diagnostics = diagnostics.ToImmutable().AddRange(result.Diagnostics) };
    }
}

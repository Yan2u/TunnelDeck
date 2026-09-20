using System.Text.Json.Nodes;

namespace TunnelDeck.Infrastructure.Persistence;

/// <summary>A deterministic, explicit migration of a documented older schema. Input is a private copy.</summary>
public interface IAppStateMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    JsonObject Migrate(JsonObject source);
}

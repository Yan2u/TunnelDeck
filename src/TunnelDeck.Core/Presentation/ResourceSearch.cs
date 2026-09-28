using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Navigation;

namespace TunnelDeck.Core.Presentation;

public sealed record ResourceSearchResult(ResourceKind Kind, Guid Id, string Name, string Description)
{
    public AppRoute Route => Kind == ResourceKind.Profile ? AppRoute.Profile(Id) : AppRoute.Server(Id);
    public string AccessibleName => $"{Name}, {Description}";
}

public static class ResourceSearch
{
    public static ImmutableArray<ResourceSearchResult> Find(AppState state, string? query, int maximumResults = 12)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumResults, 1);
        string text = query?.Trim() ?? string.Empty;
        if (text.Length == 0) { return []; }
        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var results = new List<(ResourceSearchResult Result, int Rank)>();
        foreach (ForwardProfile profile in state.Profiles)
        {
            if (!words.All(word => Contains(profile.Name, word))) { continue; }
            bool duplicate = state.Profiles.Count(item => StringComparer.OrdinalIgnoreCase.Equals(item.Name, profile.Name)) > 1;
            string description = UiText.Get("Search_Profile", UiText.Count(profile.Rules.Length, "rule"));
            if (duplicate) { description += $" · {profile.Id.ToString("N")[..8]}"; }
            results.Add((new(ResourceKind.Profile, profile.Id, profile.Name, description), Rank(profile.Name, text)));
        }
        foreach (RemoteServer server in state.Servers)
        {
            string name = ResourceDisplay.ServerName(server);
            string[] fields = [name, server.Host, server.Username];
            if (!words.All(word => fields.Any(field => Contains(field, word)))) { continue; }
            string endpoint = ResourceDisplay.ServerEndpoint(server);
            bool duplicate = state.Servers.Count(item => StringComparer.OrdinalIgnoreCase.Equals(ResourceDisplay.ServerName(item), name) &&
                StringComparer.OrdinalIgnoreCase.Equals(ResourceDisplay.ServerEndpoint(item), endpoint)) > 1;
            string description = UiText.Get("Search_Server", endpoint) + (duplicate ? $" · {server.Id.ToString("N")[..8]}" : string.Empty);
            int rank = Math.Min(Rank(name, text), 3 + Rank(server.Host, text));
            results.Add((new(ResourceKind.Server, server.Id, name, description), rank));
        }
        return results.OrderBy(item => item.Rank).ThenBy(item => item.Result.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Result.Kind).ThenBy(item => item.Result.Description, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Result.Id).Take(maximumResults).Select(item => item.Result).ToImmutableArray();
    }

    private static bool Contains(string source, string query) => source.Contains(query, StringComparison.OrdinalIgnoreCase);
    private static int Rank(string name, string query) => name.Equals(query, StringComparison.OrdinalIgnoreCase) ? 0 :
        name.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 1 : Contains(name, query) ? 2 : 6;
}

using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.ViewModels;

public sealed record SearchSuggestion(ResourceSearchResult? Result)
{
    public string Name => Result?.Name ?? "No matching profiles or servers";
    public string Description => Result?.Description ?? "Try a name, host or username.";
    public string Glyph => Result?.Kind == ResourceKind.Profile ? "\uE8F1" : Result is null ? "\uE721" : "\uE968";
    public string AccessibleName => Result?.AccessibleName ?? Name;
    public string AutomationId => Result is null ? "SearchNoResults" : $"Search{Result.Kind}_{Result.Id:N}";
    public override string ToString() => AccessibleName;
}

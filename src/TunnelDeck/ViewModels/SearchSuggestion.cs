using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.ViewModels;

public sealed record SearchSuggestion(ResourceSearchResult? Result)
{
    public string Name => Result?.Name ?? UiText.Get("SearchSuggestion_NoMatchingProfilesOrServers");
    public string Description => Result?.Description ?? UiText.Get("SearchSuggestion_TryANameHostOrUsername");
    public string Glyph => Result?.Kind == ResourceKind.Profile ? "\uE8F1" : Result is null ? "\uE721" : "\uE968";
    public string AccessibleName => Result?.AccessibleName ?? Name;
    public string AutomationId => Result is null ? "SearchNoResults" : $"Search{Result.Kind}_{Result.Id:N}";
    public override string ToString() => AccessibleName;
}

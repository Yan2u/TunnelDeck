using TunnelDeck.Core.Models;
using TunnelDeck.Core.Navigation;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.ViewModels;

public sealed record ResourceRow(ResourceKind Kind, Guid Id, string Name, string Description, bool IsPinned)
{
    public bool CanEdit { get; init; } = true;
    public bool CanMoveUp { get; init; }
    public bool CanMoveDown { get; init; }
    public AppRoute Route => Kind == ResourceKind.Profile ? AppRoute.Profile(Id) : AppRoute.Server(Id);
    public string Glyph => Kind == ResourceKind.Profile ? "\uE8F1" : "\uE968";
    public string PinGlyph => IsPinned ? "\uE77A" : "\uE718";
    public string PinLabel => IsPinned ? "Unpin" : "Pin";
    public string AccessibleName => $"{Name}, {Description}";
    public string ChoiceLabel => $"{Name} · {Description}";
    public string PinAccessibleName => $"{PinLabel} {AccessibleName}";
    public string MenuAccessibleName => $"Actions for {AccessibleName}";
    public string RowAutomationId => $"{Kind}Row_{Id:N}";
    public string PinAutomationId => $"Pin{Kind}_{Id:N}";
    public string MenuAutomationId => $"Menu{Kind}_{Id:N}";
    public string MoveUpAutomationId => $"MoveUp{Kind}_{Id:N}";
    public string MoveDownAutomationId => $"MoveDown{Kind}_{Id:N}";
    public string DragAutomationId => $"Drag{Kind}_{Id:N}";
    public string DeleteAutomationId => $"Delete{Kind}_{Id:N}";
    public override string ToString() => AccessibleName;

    public static ResourceRow FromServer(RemoteServer server, bool pinned) =>
        new(ResourceKind.Server, server.Id, ResourceDisplay.ServerName(server), ResourceDisplay.ServerEndpoint(server), pinned);

    public static ResourceRow FromProfile(ForwardProfile profile, bool pinned) =>
        new(ResourceKind.Profile, profile.Id, profile.Name, $"{profile.Rules.Length} forwarding {(profile.Rules.Length == 1 ? "rule" : "rules")}", pinned);

    public static IReadOnlyList<ResourceRow> Disambiguate(IEnumerable<ResourceRow> source)
    {
        ResourceRow[] rows = source.ToArray();
        var duplicates = rows.GroupBy(row => row.Kind == ResourceKind.Profile ? row.Name : row.Name + "\n" + row.Description, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1).SelectMany(group => group.Select(row => row.Id)).ToHashSet();
        return rows.Select(row => duplicates.Contains(row.Id) ? row with { Description = row.Description + " · " + row.Id.ToString("N")[..8] } : row).ToArray();
    }
}

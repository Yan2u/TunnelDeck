using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.ViewModels;

public sealed record RecentRow(string Name, string Route, string Time, string Outcome);
public sealed record RuleRow(Guid Id, string ServerName, string Mode, string Route, string FullRoute, string PortPolicy)
{
    public string AccessibleName => $"{ServerName}, {Mode}, {Route}, {PortPolicy}";
    public override string ToString() => AccessibleName;
    public static RuleRow Create(ForwardRule rule, AppState state) => new(rule.Id,
        ResourceDisplay.ServerName(state.Servers.First(server => server.Id == rule.ServerId)),
        ResourceDisplay.ModeName(rule.Mode), ResourceDisplay.PortRoute(rule), ResourceDisplay.FullRoute(rule),
        rule.Mode == ForwardMode.Local && rule.AutoFindAvailablePort ? "Find next available local port" : "Use requested port");
}

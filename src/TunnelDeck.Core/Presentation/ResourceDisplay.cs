using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Presentation;

public static class ResourceDisplay
{
    public static string Count(int count, string singular) => UiText.Count(count, singular);
    public static string ServerName(RemoteServer server) => string.IsNullOrWhiteSpace(server.DisplayName) ? server.Host : server.DisplayName;
    public static string ServerEndpoint(RemoteServer server) => $"{server.Username}@{new NetworkEndpoint(server.Host, server.Port)}";
    public static string ModeName(ForwardMode mode) => mode == ForwardMode.Local ? UiText.Get("RuleEditorView_LocalForwarding") : UiText.Get("RuleEditorView_RemoteForwarding");

    public static string PortRoute(ForwardRule rule, ForwardRoute? actual = null)
    {
        ForwardRoute route = actual ?? rule.GetRoute();
        return rule.Mode == ForwardMode.Local
            ? UiText.Get("ResourceDisplay_LocalRemote", route.Local.Port, route.Remote.Port)
            : UiText.Get("ResourceDisplay_RemoteLocal", route.Remote.Port, route.Local.Port);
    }

    public static string FullRoute(ForwardRule rule, ForwardRoute? actual = null)
    {
        ForwardRoute route = actual ?? rule.GetRoute();
        return rule.Mode == ForwardMode.Local ? $"{route.Local} → {route.Remote}" : $"{route.Remote} → {route.Local}";
    }
}

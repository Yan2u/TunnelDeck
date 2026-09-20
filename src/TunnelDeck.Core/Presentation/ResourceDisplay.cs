using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Presentation;

public static class ResourceDisplay
{
    public static string ServerName(RemoteServer server) => string.IsNullOrWhiteSpace(server.DisplayName) ? server.Host : server.DisplayName;
    public static string ServerEndpoint(RemoteServer server) => $"{server.Username}@{new NetworkEndpoint(server.Host, server.Port)}";
    public static string ModeName(ForwardMode mode) => mode == ForwardMode.Local ? "Local forwarding" : "Remote forwarding";

    public static string PortRoute(ForwardRule rule, ForwardRoute? actual = null)
    {
        ForwardRoute route = actual ?? rule.GetRoute();
        return rule.Mode == ForwardMode.Local
            ? $"Local :{route.Local.Port} → Remote :{route.Remote.Port}"
            : $"Remote :{route.Remote.Port} → Local :{route.Local.Port}";
    }

    public static string FullRoute(ForwardRule rule, ForwardRoute? actual = null)
    {
        ForwardRoute route = actual ?? rule.GetRoute();
        return rule.Mode == ForwardMode.Local ? $"{route.Local} → {route.Remote}" : $"{route.Remote} → {route.Local}";
    }
}

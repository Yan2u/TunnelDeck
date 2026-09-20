using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Policies;

public readonly record struct ForwardRequestKey(Guid ServerId, ForwardMode Mode, string LocalHost, int LocalPort, string RemoteHost, int RemotePort)
{
    public static ForwardRequestKey FromRule(ForwardRule rule) => new(rule.ServerId, rule.Mode,
        HostAddress.Normalize(rule.LocalHost), rule.LocalPort, HostAddress.Normalize(rule.RemoteHost), rule.RemotePort);
}

public enum DuplicateTunnelAction
{
    Create,
    Reuse,
    WaitForStop,
}

public sealed record DuplicateTunnelDecision(DuplicateTunnelAction Action, ActiveTunnel? Existing = null);

public static class DuplicateTunnelPolicy
{
    public static DuplicateTunnelDecision Evaluate(ForwardRule requested, IEnumerable<ActiveTunnel> tunnels)
    {
        ArgumentNullException.ThrowIfNull(requested);
        ArgumentNullException.ThrowIfNull(tunnels);
        ForwardRequestKey key = ForwardRequestKey.FromRule(requested);
        ActiveTunnel[] matches = tunnels.Where(tunnel => TunnelStatePolicy.HasLiveWork(tunnel.State) &&
                ForwardRequestKey.FromRule(tunnel.Requested) == key)
            .OrderBy(tunnel => tunnel.State == TunnelState.Running ? 0 : 1)
            .ThenBy(tunnel => tunnel.CreatedAt).ThenBy(tunnel => tunnel.Id).ToArray();
        ActiveTunnel? reusable = matches.FirstOrDefault(tunnel => !tunnel.StopRequested && tunnel.State != TunnelState.Stopping);
        if (reusable is not null) { return new(DuplicateTunnelAction.Reuse, reusable); }
        return matches.Length > 0 ? new(DuplicateTunnelAction.WaitForStop, matches[0]) : new(DuplicateTunnelAction.Create);
    }
}

using TunnelDeck.Core.Runtime;

namespace TunnelDeck.Core.Policies;

public static class TunnelStatePolicy
{
    public static bool HasLiveWork(TunnelState state) => state is TunnelState.Starting or TunnelState.Running or TunnelState.Reconnecting or TunnelState.Stopping;

    public static bool CanApplyStartCompletion(ActiveTunnel tunnel, long generation) =>
        !tunnel.StopRequested && tunnel.Generation == generation && tunnel.State is TunnelState.Starting or TunnelState.Reconnecting;

    public static bool CanTransition(TunnelState from, TunnelState to, bool stopRequested = false)
    {
        if (!Enum.IsDefined(from) || !Enum.IsDefined(to)) { return false; }
        if (stopRequested && to is TunnelState.Starting or TunnelState.Running or TunnelState.Reconnecting) { return false; }
        if (from == to) { return true; }
        return from switch
        {
            TunnelState.Starting => to is TunnelState.Running or TunnelState.Stopping or TunnelState.Failed or TunnelState.Cancelled,
            TunnelState.Running => to is TunnelState.Reconnecting or TunnelState.Stopping or TunnelState.Failed,
            TunnelState.Reconnecting => to is TunnelState.Running or TunnelState.Stopping or TunnelState.Failed or TunnelState.Cancelled,
            TunnelState.Stopping => to is TunnelState.Stopped or TunnelState.Failed or TunnelState.Cancelled,
            _ => false,
        };
    }
}

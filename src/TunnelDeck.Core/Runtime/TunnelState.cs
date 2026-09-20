namespace TunnelDeck.Core.Runtime;

public enum TunnelState
{
    Starting,
    Running,
    Reconnecting,
    Stopping,
    Stopped,
    Failed,
    Cancelled,
}

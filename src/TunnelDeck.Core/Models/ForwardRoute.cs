namespace TunnelDeck.Core.Models;

public sealed record ForwardRoute(NetworkEndpoint Local, NetworkEndpoint Remote)
{
    public NetworkEndpoint Listener(ForwardMode mode) => mode switch
    {
        ForwardMode.Local => Local,
        ForwardMode.Remote => Remote,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    public NetworkEndpoint Target(ForwardMode mode) => mode switch
    {
        ForwardMode.Local => Remote,
        ForwardMode.Remote => Local,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}

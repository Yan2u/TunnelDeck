namespace TunnelDeck.Core.Lifecycle;

public interface IShutdownParticipant
{
    Task ShutdownAsync(CancellationToken cancellationToken);
}

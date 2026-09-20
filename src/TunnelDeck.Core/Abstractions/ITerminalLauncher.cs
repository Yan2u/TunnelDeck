using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Abstractions;

public interface ITerminalLauncher
{
    /// <summary>Launches a user-owned terminal process. Completion does not assert SSH authentication succeeded.</summary>
    Task OpenAsync(RemoteServer server, CancellationToken cancellationToken = default);
}

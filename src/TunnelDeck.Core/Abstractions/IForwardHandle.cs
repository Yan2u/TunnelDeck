using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Abstractions;

public interface IForwardHandle : IAsyncDisposable
{
    ForwardRoute Actual { get; }
    bool IsStarted { get; }
    ForwardActivity Activity { get; }
    event Action<ForwardActivity>? ActivityChanged;
    Task StopAsync(CancellationToken cancellationToken = default);
}

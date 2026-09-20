using Microsoft.UI.Dispatching;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.Services;

public sealed class ViewDispatcher(DispatcherQueue dispatcher) : IViewDispatcher
{
    public bool HasThreadAccess => dispatcher.HasThreadAccess;
    public bool TryEnqueue(Action action) => dispatcher.TryEnqueue(() => action());
}

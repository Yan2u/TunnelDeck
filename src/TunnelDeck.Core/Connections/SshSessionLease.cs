using TunnelDeck.Core.Abstractions;

namespace TunnelDeck.Core.Connections;

/// <summary>Release this lease normally; invalidate only when a control outcome makes its whole transport unusable.</summary>
public sealed class SshSessionLease : IAsyncDisposable
{
    private readonly Func<bool, Task> _release;
    private readonly Func<Task> _invalidate;
    private readonly object _sync = new();
    private Task? _releaseTask;
    public ISshSession Session { get; }

    internal SshSessionLease(ISshSession session, Func<bool, Task> release, Func<Task> invalidate)
    {
        Session = session;
        _release = release;
        _invalidate = invalidate;
    }

    /// <summary>Retires this physical transport and notifies all of its borrowers. Its cleanup stays manager-owned.</summary>
    public Task InvalidateAsync() => _invalidate();
    public ValueTask DisposeAsync()
    {
        lock (_sync)
        {
            if (_releaseTask is null) { _releaseTask = _release(false); }
            else if (_releaseTask.IsFaulted || _releaseTask.IsCanceled) { _releaseTask = _release(true); }
            return new(_releaseTask);
        }
    }
}

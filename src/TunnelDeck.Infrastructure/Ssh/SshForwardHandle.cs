using Renci.SshNet;
using Renci.SshNet.Common;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Infrastructure.Ssh;

internal sealed class SshForwardHandle : IForwardHandle
{
    private readonly SshNetSession _owner;
    private readonly object _sync = new();
    private ForwardActivity _activity = new();
    private bool _ending;
    internal ForwardedPort Forward { get; }
    public ForwardRoute Actual { get; }
    public bool IsStarted => !_owner.Disconnected.IsCancellationRequested && Forward.IsStarted;
    public ForwardActivity Activity { get { lock (_sync) { return _activity; } } }
    public event Action<ForwardActivity>? ActivityChanged;

    public SshForwardHandle(SshNetSession owner, ForwardedPort forward, ForwardRoute actual)
    {
        _owner = owner;
        Forward = forward;
        Actual = actual;
        Forward.RequestReceived += OnRequestReceived;
        Forward.Exception += OnException;
        Forward.Closing += OnClosing;
    }

    private void OnRequestReceived(object? sender, PortForwardEventArgs args)
    {
        ForwardActivity activity;
        lock (_sync)
        {
            if (_ending) { return; }
            activity = _activity = _activity with
            {
                Revision = _activity.Revision + 1, AcceptedConnections = _activity.AcceptedConnections + 1,
                LastRequestAt = DateTimeOffset.UtcNow,
            };
        }
        Notify(activity);
    }

    private void OnException(object? sender, ExceptionEventArgs args)
    {
        ForwardActivity activity;
        lock (_sync)
        {
            if (_ending) { return; }
            activity = _activity = _activity with
            {
                Revision = _activity.Revision + 1,
                FailedConnections = _activity.FailedConnections + 1, LastFailure = ForwardFailureMapper.Request(),
                LastFailureAt = DateTimeOffset.UtcNow, ListenerStopped = !Forward.IsStarted,
            };
        }
        Notify(activity);
    }

    private void OnClosing(object? sender, EventArgs args)
    {
        ForwardActivity activity;
        lock (_sync)
        {
            if (_ending) { return; }
            _ending = true;
            activity = _activity = _activity with { Revision = _activity.Revision + 1, ListenerStopped = true };
        }
        Notify(activity);
    }

    private void Notify(ForwardActivity activity)
    {
        if (ActivityChanged is not { } handlers) { return; }
        foreach (Action<ForwardActivity> handler in handlers.GetInvocationList().Cast<Action<ForwardActivity>>())
        {
            try { handler(activity); }
            catch (Exception) { /* Diagnostics cannot interrupt SSH channel processing. */ }
        }
    }

    internal void DisposeNative()
    {
        lock (_sync) { _ending = true; }
        Forward.RequestReceived -= OnRequestReceived;
        Forward.Exception -= OnException;
        Forward.Closing -= OnClosing;
        Forward.Dispose();
    }

    public Task StopAsync(CancellationToken cancellationToken = default) => _owner.StopAsync(this, cancellationToken);
    public ValueTask DisposeAsync() => new(StopAsync());
}

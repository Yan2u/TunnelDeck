using Renci.SshNet;
using Renci.SshNet.Common;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Security;
using TunnelDeck.Core.Validation;
using TunnelDeck.Infrastructure.Security;
using SshAuthenticationMethod = Renci.SshNet.AuthenticationMethod;

namespace TunnelDeck.Infrastructure.Ssh;

internal sealed class SshNetSession : ISshSession
{
    private readonly SshClient _client;
    private readonly HostKeyVerifier _verifier;
    private readonly SshAuthenticationMethod _authentication;
    private readonly PrivateKeyFile? _key;
    private readonly SshControlSession _control;
    private readonly CancellationTokenSource _disconnected = new();
    private readonly SemaphoreSlim _forwardGate = new(1, 1);
    private readonly HashSet<SshForwardHandle> _handles = [];
    private readonly object _disposeSync = new();
    private Task? _disposeTask;

    public Guid ServerId { get; }
    public ForwardMode Mode { get; }
    public HostKeyIdentity HostKey { get; }
    public CancellationToken Disconnected { get; }

    public SshNetSession(Guid serverId, ForwardMode mode, SshClient client, HostKeyVerifier verifier,
        SshAuthenticationMethod authentication, PrivateKeyFile? key, HostKeyIdentity hostKey, TimeSpan controlTimeout)
    {
        ServerId = serverId;
        Mode = mode;
        HostKey = hostKey;
        _client = client;
        _verifier = verifier;
        _authentication = authentication;
        _key = key;
        _control = new(client, controlTimeout);
        Disconnected = _disconnected.Token;
        _client.ErrorOccurred += OnErrorOccurred;
        // Covers the gap between ConnectAsync completion and event subscription.
        if (!_client.IsConnected) { SignalDisconnected(); }
    }

    private void OnErrorOccurred(object? sender, ExceptionEventArgs args) => SignalDisconnected();

    private void SignalDisconnected()
    {
        try { _disconnected.Cancel(); }
        catch (Exception) { /* Consumer callbacks must never escape into SSH.NET's receive loop. */ }
    }

    public async Task<IForwardHandle> StartAsync(ForwardRule rule, CancellationToken cancellationToken = default)
    {
        ConfigurationValidator.ValidateRule(rule).ThrowIfInvalid();
        if (rule.ServerId != ServerId || rule.Mode != Mode) { throw new ArgumentException("The rule must belong to this server and transport mode.", nameof(rule)); }
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, Disconnected);
        await _forwardGate.WaitAsync(stopping.Token).ConfigureAwait(false);
        try
        {
            stopping.Token.ThrowIfCancellationRequested();
            ForwardedPort forward = Mode == ForwardMode.Local
                ? new ForwardedPortLocal(rule.LocalHost, (uint)rule.LocalPort, rule.RemoteHost, (uint)rule.RemotePort)
                : new ForwardedPortRemote(rule.RemoteHost, (uint)rule.RemotePort, rule.LocalHost, (uint)rule.LocalPort);
            var handle = new SshForwardHandle(this, forward, rule.GetRoute());
            // Own the candidate and its event subscriptions before Start can accept a client or fail.
            _handles.Add(handle);
            try
            {
                await _control.StartAsync(forward, stopping.Token).ConfigureAwait(false);
                stopping.Token.ThrowIfCancellationRequested();
                if (!forward.IsStarted) { throw new SshConnectionException("The listener did not start."); }
                return handle;
            }
            catch (Exception error)
            {
                if (_control.IsRetired) { SignalDisconnected(); }
                try { handle.DisposeNative(); _handles.Remove(handle); }
                catch { SignalDisconnected(); await _control.DisposeAsync().ConfigureAwait(false); }
                if (error is OperationCanceledException) { throw; }
                throw ForwardFailureMapper.Start(error, Mode);
            }
        }
        finally { _forwardGate.Release(); }
    }

    internal async Task StopAsync(SshForwardHandle handle, CancellationToken cancellationToken)
    {
        await _forwardGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_handles.Contains(handle)) { return; }
            if (!Disconnected.IsCancellationRequested)
            {
                try { await _control.StopAsync(handle.Forward, cancellationToken).ConfigureAwait(false); }
                catch { if (_control.IsRetired) { SignalDisconnected(); } throw; }
            }
            else
            {
                // Client disposal also visits its ForwardedPorts. Finish that traversal
                // before disposing a handle on another thread.
                await _control.DisposeAsync().ConfigureAwait(false);
            }
            handle.DisposeNative();
            _handles.Remove(handle);
        }
        finally { _forwardGate.Release(); }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            if (_disposeTask is null || _disposeTask.IsFaulted) { _disposeTask = Task.Run(DisposeCoreAsync); }
            return new(_disposeTask);
        }
    }

    private async Task DisposeCoreAsync()
    {
        SignalDisconnected();
        await _control.DisposeAsync().ConfigureAwait(false);
        await _forwardGate.WaitAsync().ConfigureAwait(false);
        try
        {
            foreach (SshForwardHandle handle in _handles) { handle.DisposeNative(); }
            _handles.Clear();
            _client.ErrorOccurred -= OnErrorOccurred;
            _verifier.Dispose();
            _authentication.Dispose();
            _key?.Dispose();
            _disconnected.Dispose();
        }
        finally { _forwardGate.Release(); }
    }
}

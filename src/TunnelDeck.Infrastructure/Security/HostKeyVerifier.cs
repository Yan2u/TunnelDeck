using Renci.SshNet;
using Renci.SshNet.Common;
using TunnelDeck.Core.Security;

namespace TunnelDeck.Infrastructure.Security;

/// <summary>Unknown or changed keys end the handshake; UI trust decisions happen before a new connection.</summary>
public sealed class HostKeyVerifier : IDisposable
{
    private readonly SshClient _client;
    private readonly HostKeyIdentity? _trustedKey;

    public HostKeyVerifier(SshClient client, HostKeyIdentity? trustedKey)
    {
        _client = client;
        _trustedKey = trustedKey;
        _client.HostKeyReceived += OnHostKeyReceived;
    }

    public HostKeyIdentity? PresentedKey { get; private set; }
    public bool WasRejected { get; private set; }

    private void OnHostKeyReceived(object? sender, HostKeyEventArgs args)
    {
        // Explicit rejection must happen before any fallible normalization or comparison.
        args.CanTrust = false;
        WasRejected = true;
        PresentedKey = HostKeyIdentity.FromKey(_client.ConnectionInfo.Host, _client.ConnectionInfo.Port,
            args.HostKeyName, args.HostKey);
        args.CanTrust = _trustedKey?.Matches(PresentedKey) == true;
        WasRejected = !args.CanTrust;
    }

    public void Dispose() => _client.HostKeyReceived -= OnHostKeyReceived;
}

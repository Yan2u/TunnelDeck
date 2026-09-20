using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Security;

namespace TunnelDeck.Core.Connections;

public sealed partial class SshConnectionManager
{
    private async Task<ConnectionCredentials> ReadCredentialsAsync(RemoteServer server, ConnectionCredentials? supplied,
        IProgress<ConnectionStage>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(ConnectionStage.ReadingCredentials);
        if (supplied is not null) { return supplied; }
        string? secret = null;
        if (server.CredentialReference is Guid reference)
        {
            try { secret = await Task.Run(() => _credentials.Read(reference), cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { throw new ConnectionException(ConnectionFailure.CredentialUnavailable); }
            if (secret is null) { throw new ConnectionException(ConnectionFailure.CredentialRequired); }
        }
        if (secret is null && server.AuthMethod == AuthenticationMethod.Password) { throw new ConnectionException(ConnectionFailure.CredentialRequired); }
        return new(secret);
    }

    private async Task<ISshSession> OpenTrustedAsync(RemoteServer server, ForwardMode mode, ConnectionCredentials credentials,
        IProgress<ConnectionStage>? progress, CancellationToken cancellationToken, bool allowHostApproval)
    {
        progress?.Report(ConnectionStage.Connecting);
        HostKeyIdentity presented;
        try
        {
            // Discover the negotiated algorithm by rejecting an untrusted handshake before authentication.
            ISshSession unexpected = await _factory.OpenAsync(new(server, mode, credentials, null), cancellationToken).ConfigureAwait(false);
            await unexpected.DisposeAsync().ConfigureAwait(false);
            throw new ConnectionException(ConnectionFailure.ConnectionFailed);
        }
        catch (HostKeyNotTrustedException exception) { presented = exception.PresentedKey; }
        cancellationToken.ThrowIfCancellationRequested();

        await _hostApproval.EnsureTrustedAsync(server, presented, progress, cancellationToken, allowHostApproval).ConfigureAwait(false);

        progress?.Report(ConnectionStage.VerifyingHostKey);
        try { return await _factory.OpenAsync(new(server, mode, credentials, presented), cancellationToken).ConfigureAwait(false); }
        catch (HostKeyNotTrustedException) { throw new ConnectionException(ConnectionFailure.HostKeyChanged); }
    }
}

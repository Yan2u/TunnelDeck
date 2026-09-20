using System.Diagnostics;
using System.Net.Sockets;
using Renci.SshNet;
using Renci.SshNet.Common;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;
using TunnelDeck.Infrastructure.Security;
using SshAuthenticationMethod = Renci.SshNet.AuthenticationMethod;

namespace TunnelDeck.Infrastructure.Ssh;

public sealed class SshSessionFactory : ISshSessionFactory
{
    private readonly SshConnectionOptions _options;

    public SshSessionFactory(SshConnectionOptions? options = null)
    {
        _options = options ?? new();
        _options.Validate();
    }

    public Task<ISshSession> OpenAsync(SshSessionRequest request, CancellationToken cancellationToken = default)
    {
        ConfigurationValidator.ValidateServer(request.Server).ThrowIfInvalid();
        // Key-file I/O, key decryption and SSH.NET's synchronous authentication portions stay off the UI thread.
        return Task.Run(() => OpenCoreAsync(request, cancellationToken), CancellationToken.None);
    }

    private async Task<ISshSession> OpenCoreAsync(SshSessionRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = new CancellationTokenSource(_options.ConnectTimeout);
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        long started = Stopwatch.GetTimestamp();
        PrivateKeyFile? key = null;
        SshAuthenticationMethod? authentication = null;
        SshClient? client = null;
        HostKeyVerifier? verifier = null;
        bool transferred = false;
        try
        {
            if (request.Server.AuthMethod == Core.Models.AuthenticationMethod.Password)
            {
                if (request.Credentials.Secret is null) { throw new ConnectionException(ConnectionFailure.CredentialRequired); }
                authentication = new PasswordAuthenticationMethod(request.Server.Username, request.Credentials.Secret);
            }
            else
            {
                key = ReadPrivateKey(request.Server.IdentityFilePath!, request.Credentials.Secret);
                authentication = new PrivateKeyAuthenticationMethod(request.Server.Username, key);
            }
            stopping.Token.ThrowIfCancellationRequested();
            var info = new ConnectionInfo(HostAddress.Normalize(request.Server.Host), request.Server.Port, request.Server.Username, authentication)
            {
                Timeout = _options.LibraryTimeout,
            };
            // Forwarded TCP channels do not handle the registered keepalive request event.
            // Use SSH.NET's unknown-request path, which sends CHANNEL_FAILURE. OpenSSH accepts
            // either success or failure as an alive reply. This is a public, per-connection map.
            info.ChannelRequests.Remove("keepalive@openssh.com");
            client = new(info) { KeepAliveInterval = _options.KeepAliveInterval };
            verifier = new(client, request.TrustedKey);

            // Await the library task itself. Cancelling a wrapper must never leave a late authenticated client unowned.
            await client.ConnectAsync(stopping.Token).ConfigureAwait(false);
            stopping.Token.ThrowIfCancellationRequested();
            if (Stopwatch.GetElapsedTime(started) >= _options.ConnectTimeout) { throw new TimeoutException(); }
            if (!client.IsConnected || verifier.WasRejected || verifier.PresentedKey is null)
            {
                throw new ConnectionException(ConnectionFailure.ConnectionFailed);
            }
            var session = new SshNetSession(request.Server.Id, request.Mode, client, verifier,
                authentication, key, verifier.PresentedKey, _options.ControlTimeout);
            transferred = true;
            return session;
        }
        catch (Exception error)
        {
            if (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            if (deadline.IsCancellationRequested || error is TimeoutException or SshOperationTimeoutException)
            {
                throw new ConnectionException(ConnectionFailure.TimedOut);
            }
            if (verifier is { WasRejected: true, PresentedKey: not null }) { throw new HostKeyNotTrustedException(verifier.PresentedKey); }
            if (error is ConnectionException) { throw; }
            throw new ConnectionException(error switch
            {
                SshAuthenticationException => ConnectionFailure.AuthenticationFailed,
                SocketException socket when socket.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain => ConnectionFailure.NameResolutionFailed,
                _ => ConnectionFailure.ConnectionFailed,
            });
        }
        finally
        {
            if (!transferred)
            {
                try { client?.Dispose(); }
                finally { verifier?.Dispose(); authentication?.Dispose(); key?.Dispose(); }
            }
        }
    }

    private static PrivateKeyFile ReadPrivateKey(string path, string? passphrase)
    {
        FileStream stream;
        try { stream = File.OpenRead(path); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            throw new ConnectionException(ConnectionFailure.KeyFileUnreadable);
        }
        using (stream)
        {
            try { return new PrivateKeyFile(stream, passphrase); }
            catch (SshPassPhraseNullOrEmptyException) { throw new ConnectionException(ConnectionFailure.CredentialRequired); }
            catch (Exception) { throw new ConnectionException(ConnectionFailure.KeyUnlockFailed); }
        }
    }
}

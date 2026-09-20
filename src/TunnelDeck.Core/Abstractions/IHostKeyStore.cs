using TunnelDeck.Core.Security;

namespace TunnelDeck.Core.Abstractions;

public interface IHostKeyStore
{
    Task<TrustedHostKey?> FindAsync(string host, int port, string algorithm, CancellationToken cancellationToken = default);
    Task TrustAsync(TrustedHostKey key, CancellationToken cancellationToken = default);

    /// <summary>Accepts a first-use key without overwriting a key saved by another concurrent request.</summary>
    Task TrustNewAsync(TrustedHostKey key, CancellationToken cancellationToken = default);
}

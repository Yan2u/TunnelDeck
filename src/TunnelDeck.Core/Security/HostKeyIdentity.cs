using System.Security.Cryptography;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Security;

public sealed record HostKeyIdentity(string Host, int Port, string Algorithm, string Fingerprint)
{
    public static HostKeyIdentity FromKey(string host, int port, string algorithm, ReadOnlySpan<byte> publicKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);
        if (publicKey.IsEmpty) { throw new ArgumentException("A host key cannot be empty.", nameof(publicKey)); }

        string fingerprint = "SHA256:" + Convert.ToBase64String(SHA256.HashData(publicKey)).TrimEnd('=');
        return new HostKeyIdentity(HostAddress.Normalize(host), port, algorithm, fingerprint);
    }

    public bool Matches(HostKeyIdentity candidate) =>
        StringComparer.Ordinal.Equals(HostAddress.Normalize(Host), HostAddress.Normalize(candidate.Host)) &&
        Port == candidate.Port &&
        StringComparer.Ordinal.Equals(Algorithm, candidate.Algorithm) &&
        StringComparer.Ordinal.Equals(Fingerprint, candidate.Fingerprint);

}

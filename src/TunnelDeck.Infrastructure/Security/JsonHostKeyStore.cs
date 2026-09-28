using System.Collections.Immutable;
using System.Text.Json.Serialization;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Security;
using TunnelDeck.Core.Validation;
using TunnelDeck.Infrastructure.Persistence;

namespace TunnelDeck.Infrastructure.Security;

/// <summary>Trust is persisted independently. Damaged/unknown trust files fail closed; backups are never silently trusted.</summary>
public sealed class JsonHostKeyStore : IHostKeyStore
{
    private readonly AtomicJsonFile _file;

    public JsonHostKeyStore(AppDataDirectory directory) => _file = new(directory.HostKeysPath);

    public async Task<TrustedHostKey?> FindAsync(string host, int port, string algorithm, CancellationToken cancellationToken = default)
    {
        if (!HostAddress.IsValidHost(host) || port is < 1 or > 65535 || !ValidAlgorithm(algorithm))
        {
            throw new ArgumentException(UiText.Invariant("Diagnostic_JsonHostKeyStore_AValidHostSSHPortAndHostKey"));
        }
        using IDisposable lease = await _file.AcquireAsync(cancellationToken).ConfigureAwait(false);
        HostKeyDocument document = await ReadUnderLockAsync(cancellationToken).ConfigureAwait(false);
        string normalizedHost = HostAddress.Normalize(host);
        return document.Keys.FirstOrDefault(key => key.Identity.Port == port && key.Identity.Algorithm == algorithm &&
            HostAddress.Normalize(key.Identity.Host) == normalizedHost);
    }

    /// <summary>Call only after explicit host-key approval. Configuration edits/imports never call this method.</summary>
    public Task TrustAsync(TrustedHostKey key, CancellationToken cancellationToken = default) =>
        WriteTrustAsync(key, allowReplacement: true, cancellationToken);

    public Task TrustNewAsync(TrustedHostKey key, CancellationToken cancellationToken = default) =>
        WriteTrustAsync(key, allowReplacement: false, cancellationToken);

    private async Task WriteTrustAsync(TrustedHostKey key, bool allowReplacement, CancellationToken cancellationToken)
    {
        ValidateKey(key);
        key = key with { Identity = key.Identity with { Host = HostAddress.Normalize(key.Identity.Host) } };
        using IDisposable lease = await _file.AcquireAsync(cancellationToken).ConfigureAwait(false);
        HostKeyDocument document = await ReadUnderLockAsync(cancellationToken).ConfigureAwait(false);
        TrustedHostKey? existing = document.Keys.FirstOrDefault(item => SameEndpoint(item.Identity, key.Identity));
        if (existing?.Identity.Matches(key.Identity) == true) { return; }
        if (existing is not null && !allowReplacement) { throw new ConnectionException(ConnectionFailure.HostKeyChanged); }
        if (document.Revision == long.MaxValue) { throw VersionedJson.Unsupported(); }
        HostKeyDocument candidate = document with
        {
            Revision = document.Revision + 1,
            Keys = existing is null ? document.Keys.Add(key) : document.Keys.Replace(existing, key),
        };
        await _file.WriteAsync(VersionedJson.Serialize(candidate), preserveBackup: false, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HostKeyDocument> ReadUnderLockAsync(CancellationToken cancellationToken)
    {
        byte[]? bytes = await _file.ReadAsync(backup: false, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            if (await _file.ReadAsync(backup: true, cancellationToken).ConfigureAwait(false) is not null) { throw VersionedJson.Invalid(); }
            return new();
        }
        using var json = VersionedJson.Parse(bytes);
        if (VersionedJson.ReadSchema(json.RootElement, 1) != 1) { throw VersionedJson.Unsupported(); }
        HostKeyDocument document = VersionedJson.Deserialize<HostKeyDocument>(json.RootElement);
        if (document.Revision < 0 || document.Keys.IsDefault) { throw VersionedJson.Invalid(); }
        var identities = new HashSet<(string Host, int Port, string Algorithm)>();
        foreach (TrustedHostKey key in document.Keys)
        {
            ValidateKey(key);
            if (!identities.Add((HostAddress.Normalize(key.Identity.Host), key.Identity.Port, key.Identity.Algorithm))) { throw VersionedJson.Invalid(); }
        }
        return document;
    }

    private static bool SameEndpoint(HostKeyIdentity first, HostKeyIdentity second) =>
        first.Port == second.Port && first.Algorithm == second.Algorithm &&
        HostAddress.Normalize(first.Host) == HostAddress.Normalize(second.Host);

    private static bool ValidAlgorithm(string? algorithm) => !string.IsNullOrWhiteSpace(algorithm) &&
        algorithm.All(character => character is >= '!' and <= '~');

    private static void ValidateKey(TrustedHostKey? key)
    {
        if (key?.Identity is not { } identity || !HostAddress.IsValidHost(identity.Host) || identity.Port is < 1 or > 65535 ||
            !ValidAlgorithm(identity.Algorithm) || key.TrustedAt.Offset != TimeSpan.Zero ||
            identity.Fingerprint is not { Length: 50 } || !identity.Fingerprint.StartsWith("SHA256:", StringComparison.Ordinal))
        {
            throw VersionedJson.Invalid();
        }
        Span<byte> fingerprint = stackalloc byte[32];
        if (!Convert.TryFromBase64String(identity.Fingerprint[7..] + "=", fingerprint, out int length) || length != 32)
        {
            throw VersionedJson.Invalid();
        }
    }

    private sealed record HostKeyDocument
    {
        [JsonRequired] public int SchemaVersion { get; init; } = 1;
        [JsonRequired] public long Revision { get; init; }
        [JsonRequired] public ImmutableArray<TrustedHostKey> Keys { get; init; } = [];
    }
}

using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;

namespace TunnelDeck.Core.Persistence;

public sealed partial class ConfigurationService
{
    /// <summary>Persists configuration and credentials together. M5 adds active-connection edit guards above this layer.</summary>
    public Task<ConfigurationCommitResult> SaveServerAsync(RemoteServer server, CredentialChange credentialChange,
        long? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(credentialChange);
        return EnqueueAsync(async () =>
        {
            using IDisposable lease = await transactionLock.AcquireAsync(cancellationToken).ConfigureAwait(false);
            AppState before = RequireWritable(expectedRevision);
            RemoteServer? existing = before.Servers.FirstOrDefault(item => item.Id == server.Id);
            if (credentialChange.Kind == CredentialChangeKind.Keep && existing is not null && existing.AuthMethod != server.AuthMethod)
            {
                throw new ArgumentException(UiText.Invariant("Diagnostic_ConfigurationServiceServers_ChooseRememberOrForgetWhenChangingTheAuthentication"), nameof(credentialChange));
            }
            Guid? stagedReference = credentialChange.Kind == CredentialChangeKind.Remember ? Guid.NewGuid() : null;
            RemoteServer saved = server with
            {
                CredentialReference = credentialChange.Kind switch
                {
                    CredentialChangeKind.Keep => existing?.CredentialReference,
                    CredentialChangeKind.Forget => null,
                    CredentialChangeKind.Remember => stagedReference,
                    _ => throw new ArgumentOutOfRangeException(nameof(credentialChange)),
                },
            };
            AppState candidate = before with
            {
                Servers = existing is null ? before.Servers.Add(saved) : before.Servers.Replace(existing, saved),
                Order = existing is null ? ResourceOrderService.Add(before.Order, ResourceKind.Server, server.Id) : before.Order,
            };
            return await CommitCandidateAsync(before, candidate, stagedReference, credentialChange.Secret, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }
}

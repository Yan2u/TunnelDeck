using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Security;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Connections;

/// <summary>Coalesces first-use approval by exact key, without sharing authentication or physical transports.</summary>
internal sealed class HostKeyApprovalCoordinator(IHostKeyStore store, IHostKeyApproval approval)
{
    private readonly object _sync = new();
    private readonly Dictionary<HostKeyIdentity, PendingApproval> _pending = [];

    public async Task EnsureTrustedAsync(RemoteServer server, HostKeyIdentity presented,
        IProgress<ConnectionStage>? progress, CancellationToken cancellationToken, bool allowPrompt = true)
    {
        HostKeyIdentity identity = presented with { Host = HostAddress.Normalize(presented.Host) };
        if (await IsTrustedAsync(identity, cancellationToken).ConfigureAwait(false)) { return; }
        if (!allowPrompt) { throw new ConnectionException(ConnectionFailure.HostKeyDeclined); }
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PendingApproval? pending = null;
            Task? draining = null;
            lock (_sync)
            {
                if (_pending.TryGetValue(identity, out PendingApproval? existing) && existing.Cleanup is not null)
                {
                    draining = existing.Cleanup;
                }
                else
                {
                    if (existing is null)
                    {
                        existing = new(identity);
                        _pending.Add(identity, existing);
                        var request = new HostKeyApprovalRequest(ResourceDisplay.ServerName(server), server.Username, identity);
                        existing.Work = Task.Run(() => ApproveAsync(request, existing.Stop.Token));
                    }
                    pending = existing;
                    pending.Waiters++;
                }
            }
            if (draining is not null)
            {
                await draining.WaitAsync(cancellationToken).ConfigureAwait(false);
                continue;
            }
            try
            {
                progress?.Report(ConnectionStage.AwaitingHostApproval);
                await pending!.Work.WaitAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                return;
            }
            finally { await ReleaseAsync(pending!).ConfigureAwait(false); }
        }
    }

    private async Task<bool> IsTrustedAsync(HostKeyIdentity identity, CancellationToken cancellationToken)
    {
        TrustedHostKey? trusted;
        try { trusted = await store.FindAsync(identity.Host, identity.Port, identity.Algorithm, cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new ConnectionException(ConnectionFailure.TrustStoreUnavailable); }
        if (trusted is null) { return false; }
        if (!trusted.Identity.Matches(identity)) { throw new ConnectionException(ConnectionFailure.HostKeyChanged); }
        return true;
    }

    private async Task ApproveAsync(HostKeyApprovalRequest request, CancellationToken cancellationToken)
    {
        // A preceding approval may have finished between the caller's read and joining this operation.
        if (await IsTrustedAsync(request.Key, cancellationToken).ConfigureAwait(false)) { return; }
        bool accepted = await approval.ConfirmAsync(request, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (!accepted) { throw new ConnectionException(ConnectionFailure.HostKeyDeclined); }
        try { await store.TrustNewAsync(new(request.Key, DateTimeOffset.UtcNow), cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (ConnectionException) { throw; }
        catch (Exception) { throw new ConnectionException(ConnectionFailure.TrustStoreUnavailable); }
    }

    private Task ReleaseAsync(PendingApproval pending)
    {
        lock (_sync)
        {
            pending.Waiters--;
            if (pending.Waiters != 0) { return Task.CompletedTask; }
            // Keep the key registered until even a late, cancellation-ignoring prompt is drained.
            return pending.Cleanup ??= Task.Run(() => DrainAsync(pending));
        }
    }

    private async Task DrainAsync(PendingApproval pending)
    {
        try { await pending.Stop.CancelAsync().ConfigureAwait(false); }
        finally
        {
            try { await pending.Work.ConfigureAwait(false); }
            catch (Exception) { /* Each waiter owns its result; this operation owns prompt completion. */ }
            lock (_sync) { _pending.Remove(pending.Identity); }
            pending.Stop.Dispose();
        }
    }

    private sealed class PendingApproval(HostKeyIdentity identity)
    {
        public HostKeyIdentity Identity { get; } = identity;
        public CancellationTokenSource Stop { get; } = new();
        public Task Work { get; set; } = null!;
        public Task? Cleanup { get; set; }
        public int Waiters { get; set; }
    }
}

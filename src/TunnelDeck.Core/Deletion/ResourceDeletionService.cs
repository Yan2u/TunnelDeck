using System.Collections.Immutable;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Deletion;

/// <summary>Confirmation is supplied by the UI. Revalidation never asks for input while resource gates are held.</summary>
public sealed class ResourceDeletionService(ConfigurationService configuration, SshConnectionManager connections,
    TunnelManager tunnels, TunnelActivityRecorder activity) : IShutdownParticipant
{
    private readonly object _sync = new();
    private readonly SemaphoreSlim _execution = new(1, 1);
    private readonly CancellationTokenSource _closing = new();
    private readonly HashSet<Task> _work = [];
    private bool _accepting = true;

    public DeletionImpact CalcServerDeletionImpact(IEnumerable<Guid> serverIds) => Calculate(new(ResourceKind.Server, Normalize(serverIds)));
    public DeletionImpact CalcProfileDeletionImpact(IEnumerable<Guid> profileIds) => Calculate(new(ResourceKind.Profile, Normalize(profileIds)));
    public DeletionImpact CalcAllDeletionImpact(ResourceKind kind) => Calculate(new(kind, [], true));

    private static ImmutableArray<Guid> Normalize(IEnumerable<Guid> ids)
    {
        ImmutableArray<Guid> normalized = [.. ids.Distinct().Order()];
        if (normalized.Contains(Guid.Empty)) { throw new ArgumentException(UiText.Invariant("Diagnostic_ResourceDeletionService_DeletionRequiresNonEmptyResourceIDs"), nameof(ids)); }
        return normalized;
    }

    private DeletionImpact Calculate(DeletionRequest request, AppState? state = null) => DeletionImpactCalculator.Calculate(
        state ?? configuration.Current ?? throw new InvalidOperationException(UiText.Invariant("Diagnostic_ResourceDeletionService_LoadSavedDataBeforeDeletingResources")),
        tunnels.Current, connections.Work.Current, request);

    public Task<DeletionResult> DeleteConfirmedAsync(DeletionImpact confirmed, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (!_accepting) { return Task.FromResult(new DeletionResult(DeletionOutcome.Cancelled, confirmed, [], UiText.Invariant("Diagnostic_ResourceDeletionService_TunnelDeckIsClosingNothingWasDeleted"))); }
            Task<DeletionResult> work = Task.Run(() => ExecuteAsync(confirmed, cancellationToken));
            _work.Add(work);
            _ = work.ContinueWith(done => { lock (_sync) { _work.Remove(done); } }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return work;
        }
    }

    private async Task<DeletionResult> ExecuteAsync(DeletionImpact confirmed, CancellationToken cancellationToken)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closing.Token);
        CancellationToken token = stopping.Token;
        var stopped = new HashSet<Guid>();
        DeletionImpact latest = confirmed;
        bool held = false, stoppingStarted = false;
        try
        {
            await _execution.WaitAsync(token).ConfigureAwait(false);
            held = true;
            latest = Calculate(confirmed.Request);
            // A bulk request includes newly created resources; never block only the old list.
            Guid[] ids = confirmed.Request.Kind == ResourceKind.Server
                ? latest.Servers.Select(server => server.Id).ToArray() : latest.Profiles.Select(profile => profile.Id).ToArray();
            using ServerWorkBarrier barrier = connections.Work.Block(confirmed.Request.Kind, ids);
            using IDisposable? serverGates = confirmed.Request.Kind == ResourceKind.Server
                ? await connections.LockServersAsync(ids, token).ConfigureAwait(false) : null;

            DeletionResult result = await configuration.CommitDeletionAsync(async state =>
            {
                latest = Calculate(confirmed.Request, state);
                IEnumerable<Guid> currentIds = latest.Request.Kind == ResourceKind.Server
                    ? latest.Servers.Select(server => server.Id) : latest.Profiles.Select(profile => profile.Id);
                if (!barrier.Ids.SetEquals(currentIds) || !DeletionImpactCalculator.SameEffect(confirmed, latest))
                { return new(new(DeletionOutcome.NeedsConfirmation, latest, [], UiText.Invariant("Diagnostic_ResourceDeletionService_TheAffectedResourcesOrRunningWorkChangedReview"))); }
                if (latest.ResourceCount == 0) { return new(new(DeletionOutcome.Deleted, latest, [], string.Empty, state, [])); }
                token.ThrowIfCancellationRequested();
                if (confirmed.Request.Kind == ResourceKind.Server)
                {
                    stoppingStarted = true;
                    await connections.Work.CancelServersAsync(barrier.Ids).ConfigureAwait(false);
                    var beforeStop = latest.Tunnels.Select(tunnel => tunnel.Id).ToHashSet();
                    foreach (var tunnel in tunnels.Current.Tunnels.Where(tunnel => barrier.Ids.Contains(tunnel.ServerId) && DeletionImpactCalculator.IsLive(tunnel)))
                    { beforeStop.Add(tunnel.Id); }
                    var results = (await Task.WhenAll(ids.Select(id => tunnels.StopServerAsync(id))).ConfigureAwait(false)).SelectMany(group => group).ToArray();
                    foreach (Guid id in beforeStop)
                    {
                        if (!tunnels.Current.Tunnels.Any(tunnel => tunnel.Id == id && DeletionImpactCalculator.IsLive(tunnel))) { stopped.Add(id); }
                    }
                    if (results.Any(result => !result.Succeeded))
                    {
                        return new(new(DeletionOutcome.StopFailed, latest, [.. stopped],
                            UiText.Invariant("Diagnostic_ResourceDeletionService_NothingWasDeletedCouldNotBeFullyStopped", StoppedSummary(stopped.Count), results.Count(result => !result.Succeeded))));
                    }
                    try { await connections.StopConnectionsAsync(ids).ConfigureAwait(false); }
                    catch (Exception)
                    {
                        return new(new(DeletionOutcome.StopFailed, latest, [.. stopped],
                            UiText.Invariant("Diagnostic_ResourceDeletionService_NothingWasDeletedButSSHConnectionCleanupIs", StoppedSummary(stopped.Count))));
                    }
                }
                token.ThrowIfCancellationRequested();
                return new(new(DeletionOutcome.Deleted, latest, [.. stopped], string.Empty), DeletionImpactCalculator.Remove(state, latest));
            }, token).ConfigureAwait(false);
            barrier.Deleted = result.Succeeded;
            // Activity writers were queued behind the deletion transaction; never wait for them inside it.
            if (result.Succeeded)
            {
                try
                {
                    if (await activity.FlushAsync(CancellationToken.None).ConfigureAwait(false))
                    {
                        var deletedServers = result.Impact.Servers.Select(server => server.Id).ToHashSet();
                        // A retry can commit after a previous attempt already stopped these rows.
                        // Their history must be durable before any terminal row is forgotten.
                        foreach (var tunnel in tunnels.Current.Tunnels.Where(tunnel =>
                            deletedServers.Contains(tunnel.ServerId) && !DeletionImpactCalculator.IsLive(tunnel)))
                        { await tunnels.RemoveAsync(tunnel.Id).ConfigureAwait(false); }
                    }
                }
                catch (Exception) { /* A completed configuration commit cannot become a failed deletion. */ }
            }
            return result;
        }
        catch (OperationCanceledException)
        {
            return new(DeletionOutcome.Cancelled, latest, [.. stopped], stoppingStarted
                ? UiText.Invariant("Diagnostic_ResourceDeletionService_DeletionWasCancelledConfigurationWasKept", StoppedSummary(stopped.Count))
                : UiText.Invariant("Diagnostic_ResourceDeletionService_DeletionWasCancelledNothingWasDeleted"));
        }
        catch (Exception)
        {
            return new(DeletionOutcome.SaveFailed, latest, [.. stopped], stoppingStarted
                ? UiText.Invariant("Diagnostic_ResourceDeletionService_NothingWasDeletedButTheConfigurationCouldNot", StoppedSummary(stopped.Count))
                : UiText.Invariant("Diagnostic_ResourceDeletionService_NothingWasDeletedTheConfigurationCouldNotBe"));
        }
        finally { if (held) { _execution.Release(); } }
    }

    private static string StoppedSummary(int count) => UiText.Invariant(count == 1 ? "Diagnostic_StoppedOne" : "Diagnostic_StoppedOther", count);

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        Task[] pending;
        lock (_sync) { _accepting = false; pending = _work.ToArray(); }
        await _closing.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
    }
}

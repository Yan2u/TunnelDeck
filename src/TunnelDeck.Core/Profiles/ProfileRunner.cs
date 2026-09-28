using System.Collections.Immutable;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Snapshots;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Profiles;

/// <summary>Window-lifetime batch commands over saved snapshots. Leaving a page never abandons an accepted start.</summary>
public sealed partial class ProfileRunner : IShutdownParticipant, IDisposable
{
    private readonly ConfigurationService _configuration;
    private readonly TunnelManager _tunnels;
    private readonly TunnelActivityRecorder _activity;
    private readonly Func<RemoteServer, string, CancellationToken, Task<ConnectionCredentials?>> _credentials;
    private readonly bool _networkAllowed;
    private readonly TimeProvider _clock;
    private readonly object _sync = new();
    private readonly Dictionary<Guid, Operation> _operations = [];
    private readonly Dictionary<Guid, ProfileRunSnapshot> _current = [];
    private readonly HashSet<Task> _work = [];
    private long _revision;
    private bool _closing;
    private Task? _shutdown;
    public event Action<ProfileRunSnapshot>? StateChanged;

    public ProfileRunner(ConfigurationService configuration, TunnelManager tunnels, TunnelActivityRecorder activity,
        Func<RemoteServer, string, CancellationToken, Task<ConnectionCredentials?>> credentials, bool networkAllowed = true, TimeProvider? clock = null)
    { _configuration = configuration; _tunnels = tunnels; _activity = activity; _credentials = credentials; _networkAllowed = networkAllowed; _clock = clock ?? TimeProvider.System; }

    public ProfileRunSnapshot? Get(Guid profileId) { lock (_sync) { return _current.GetValueOrDefault(profileId); } }
    public Task<ProfileRunSnapshot> StartAllAsync(Guid profileId) => Start(profileId, null, false);
    public Task<ProfileRunSnapshot> StartRuleAsync(Guid profileId, Guid ruleId) => Start(profileId, ruleId, false);
    public Task<ProfileRunSnapshot> RetryFailedAsync(Guid profileId) => Start(profileId, null, true);

    private Task<ProfileRunSnapshot> Start(Guid profileId, Guid? ruleId, bool failedOnly)
    {
        Operation operation;
        ProfileRunSnapshot initial;
        lock (_sync)
        {
            if (_closing || !_networkAllowed || _configuration.IsReadOnly) { throw new InvalidOperationException(UiText.Invariant("Diagnostic_ProfileRunner_ProfileForwardingIsCurrentlyUnavailable")); }
            if (_operations.TryGetValue(profileId, out Operation? running)) { return running.Work; }
            AppState state = _configuration.Current!;
            ForwardProfile profile = state.Profiles.FirstOrDefault(item => item.Id == profileId)
                ?? throw new InvalidOperationException(UiText.Invariant("Diagnostic_ProfileRunner_ThisProfileIsNoLongerAvailable"));
            ProfileLaunchSnapshot launch = ProfileSnapshotFactory.Capture(profile, state.Servers, _clock);
            ProfileRunSnapshot? previous = _current.GetValueOrDefault(profileId);
            var selected = launch.Rules.Where(rule => ruleId is null || rule.Requested.Id == ruleId)
                .Where(rule => !failedOnly || previous?.Rules.Any(item => item.Launch.Requested.Id == rule.Requested.Id && item.Outcome == ProfileRuleOutcome.Failed) == true)
                .Select(rule => rule.Requested.Id).ToHashSet();
            initial = new(profileId, Guid.NewGuid(), profile.Name, ++_revision, selected.Count > 0, [.. launch.Rules.Select(rule =>
                selected.Contains(rule.Requested.Id) ? new ProfileRuleResult(rule, ProfileRuleOutcome.Starting) :
                previous?.Rules.FirstOrDefault(item => item.Launch.Requested == rule.Requested) ?? new(rule, ProfileRuleOutcome.Pending))]);
            _current[profileId] = initial;
            if (selected.Count == 0) { return Task.FromResult(initial); }
            operation = new(initial, selected, previous);
            try
            {
                foreach (var group in launch.Rules.Where(rule => selected.Contains(rule.Requested.Id)).GroupBy(rule => rule.Server.Id))
                {
                    operation.ServerWork.Add(group.Key, _tunnels.Work.Begin(group.First().Server, ServerWorkKind.Profile, group, operation.Cancellation.Token));
                }
            }
            catch
            {
                foreach (ServerWorkLease work in operation.ServerWork.Values) { work.Dispose(); }
                operation.Cancellation.Dispose();
                if (previous is null) { _current.Remove(profileId); } else { _current[profileId] = previous; }
                throw;
            }
            _operations.Add(profileId, operation);
            operation.Work = Task.Run(() => ExecuteAsync(operation));
            _work.Add(operation.Work);
            _ = operation.Work.ContinueWith(completed => { lock (_sync) { _work.Remove(completed); } }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        Notify(initial);
        return operation.Work;
    }

    private async Task<ProfileRunSnapshot> ExecuteAsync(Operation operation)
    {
        await Task.WhenAll(operation.ServerWork.Select(async pair =>
        {
            using ServerWorkLease work = pair.Value;
            await Task.WhenAll(operation.Snapshot.Rules
                .Where(rule => operation.Selected.Contains(rule.Launch.Requested.Id) && rule.Launch.Server.Id == pair.Key)
                .Select(rule => RunRuleAsync(operation, rule.Launch, work))).ConfigureAwait(false);
        })).ConfigureAwait(false);
        ProfileRunSnapshot finished;
        Task cancelling;
        lock (_sync)
        {
            finished = operation.Snapshot with { IsBusy = false, Revision = ++_revision };
            _current[finished.ProfileId] = finished;
            _operations.Remove(finished.ProfileId);
            operation.Inputs.Clear();
            cancelling = operation.Cancelling ?? Task.CompletedTask;
        }
        await cancelling.ConfigureAwait(false);
        operation.Cancellation.Dispose();
        Notify(finished);
        return finished;
    }

    private void Publish(Operation operation, ProfileRuleResult result)
    {
        ProfileRunSnapshot snapshot;
        lock (_sync)
        {
            snapshot = operation.Snapshot with
            {
                Revision = ++_revision,
                Rules = [.. operation.Snapshot.Rules.Select(item => item.Launch.Requested.Id == result.Launch.Requested.Id ? result : item)],
            };
            operation.Snapshot = snapshot;
            _current[snapshot.ProfileId] = snapshot;
        }
        if (result.Outcome == ProfileRuleOutcome.Started) { _activity.RecordProfileUse(snapshot.OperationId, snapshot.ProfileId, _clock.GetUtcNow()); }
        Notify(snapshot);
    }

    public async Task CancelAsync(Guid profileId)
    {
        Task cancellation, work;
        lock (_sync)
        {
            if (!_operations.TryGetValue(profileId, out Operation? operation)) { return; }
            cancellation = operation.Cancelling ??= operation.Cancellation.CancelAsync(); work = operation.Work;
        }
        await cancellation.ConfigureAwait(false); await work.ConfigureAwait(false);
    }

    public Task ShutdownAsync(CancellationToken cancellationToken)
    {
        lock (_sync)
        {
            _closing = true;
            foreach (Operation operation in _operations.Values) { operation.Cancelling ??= operation.Cancellation.CancelAsync(); }
            _shutdown ??= Task.WhenAll(_work.ToArray());
            return _shutdown.WaitAsync(cancellationToken);
        }
    }
    private void Notify(ProfileRunSnapshot snapshot)
    {
        if (StateChanged is not { } handlers) { return; }
        foreach (Action<ProfileRunSnapshot> handler in handlers.GetInvocationList().Cast<Action<ProfileRunSnapshot>>()) { try { handler(snapshot); } catch (Exception) { } }
    }
    public void Dispose() { lock (_sync) { _closing = true; foreach (Operation operation in _operations.Values) { operation.Cancelling ??= operation.Cancellation.CancelAsync(); } } }

    private sealed class Operation(ProfileRunSnapshot snapshot, HashSet<Guid> selected, ProfileRunSnapshot? previous)
    {
        public ProfileRunSnapshot Snapshot { get; set; } = snapshot;
        public HashSet<Guid> Selected { get; } = selected;
        public ProfileRunSnapshot? Previous { get; } = previous;
        public CancellationTokenSource Cancellation { get; } = new();
        public Dictionary<Guid, Task<ConnectionCredentials?>> Inputs { get; } = [];
        public Dictionary<Guid, ServerWorkLease> ServerWork { get; } = [];
        public Task<ProfileRunSnapshot> Work { get; set; } = null!;
        public Task? Cancelling { get; set; }
    }
}

using System.Collections.ObjectModel;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Presentation;

/// <summary>Window-lifetime commands and stable rows, independent of WinUI. All published UI changes use the dispatcher.</summary>
public sealed partial class TunnelWorkspace : ObservableModel, IShutdownParticipant, IDisposable
{
    private readonly TunnelManager _tunnels;
    private readonly TunnelActivityRecorder _activity;
    private readonly ConfigurationService _configuration;
    private readonly IViewDispatcher _dispatcher;
    private readonly Func<RemoteServer, string, CancellationToken, Task<ConnectionCredentials?>> _credentials;
    private readonly TimeProvider _clock;
    private readonly ObservableCollection<TunnelItem> _items = [];
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _commandSync = new();
    private readonly HashSet<Task> _commands = [];
    private AppState _state;
    private bool _closing, _disposed, _available;
    private long _revision = -1;
    private string _commandError = string.Empty, _activityError = string.Empty;
    public bool IsPreview { get; }
    public QuickForwardModel Quick { get; } = new();
    public ReadOnlyObservableCollection<TunnelItem> Items { get; }
    public string RuntimeStatus => $"{_items.Count(item => item.Value.IsRunning)} active";
    public bool HasTunnels => _items.Count > 0;
    public bool CanStopAll => !_closing && _items.Any(item => item.CanStop);
    public string CommandError { get => _commandError; private set => SetProperty(ref _commandError, value); }
    public string ActivityError { get => _activityError; private set => SetProperty(ref _activityError, value); }
    public event Action? RuntimeChanged;

    public TunnelWorkspace(TunnelManager tunnels, TunnelActivityRecorder activity, ConfigurationService configuration,
        IViewDispatcher dispatcher, Func<RemoteServer, string, CancellationToken, Task<ConnectionCredentials?>> credentials,
        bool preview = false, TimeProvider? clock = null)
    {
        _tunnels = tunnels; _activity = activity; _configuration = configuration; _dispatcher = dispatcher;
        _credentials = credentials; IsPreview = preview; _clock = clock ?? TimeProvider.System;
        _state = configuration.Current ?? new(); _available = !configuration.IsReadOnly;
        Items = new(_items);
        tunnels.StateChanged += OnRuntimeChanged;
        configuration.StateChanged += OnConfigurationChanged;
        activity.ErrorChanged += OnActivityErrorChanged;
        Quick.Reconcile(_state, IsPreview, _available);
        ApplySnapshot(tunnels.Current);
    }

    private void OnRuntimeChanged(TunnelManagerSnapshot snapshot) => Post(() => ApplySnapshot(snapshot));
    private void OnConfigurationChanged(AppState state) => Post(() => ApplyConfiguration(state));
    private void OnActivityErrorChanged() => Post(() => ActivityError = _activity.Error);
    private void Post(Action action) => _dispatcher.TryEnqueue(() => { if (!_disposed) { action(); } });

    public void SetAvailable(bool available) { _available = available; _state = _configuration.Current ?? _state; Quick.Reconcile(_state, IsPreview, available && !_closing); }
    private void ApplyConfiguration(AppState state)
    {
        if (state.Revision < _state.Revision) { return; }
        _state = state;
        Quick.Reconcile(state, IsPreview, _available && !_closing);
        foreach (TunnelItem item in _items) { item.Update(item.Value, state); }
    }

    private void ApplySnapshot(TunnelManagerSnapshot snapshot)
    {
        if (_disposed || IsPreview || snapshot.Revision <= _revision) { return; }
        _revision = snapshot.Revision;
        UpdateItems(snapshot.Tunnels);
    }

    public void SetPreview(IEnumerable<ActiveTunnel> items)
    {
        if (!IsPreview) { throw new InvalidOperationException("Only an isolated preview can supply sample rows."); }
        UpdateItems(items);
    }

    private void UpdateItems(IEnumerable<ActiveTunnel> values)
    {
        ActiveTunnel[] snapshot = values.ToArray();
        var present = snapshot.Select(value => value.Id).ToHashSet();
        for (int index = _items.Count - 1; index >= 0; index--) { if (!present.Contains(_items[index].Id)) { _items.RemoveAt(index); } }
        foreach (ActiveTunnel value in snapshot)
        {
            TunnelItem? item = _items.FirstOrDefault(row => row.Id == value.Id);
            if (item is null) { _items.Add(new(value, _state, _clock.GetUtcNow(), IsPreview)); }
            else { item.Update(value, _state); }
        }
        OnPropertyChanged(nameof(RuntimeStatus)); OnPropertyChanged(nameof(HasTunnels)); OnPropertyChanged(nameof(CanStopAll));
        RuntimeChanged?.Invoke();
    }

    public void RefreshDetails(TunnelItem item)
    {
        ActiveTunnel? current = IsPreview ? item.Value : _tunnels.Current.Tunnels.FirstOrDefault(tunnel => tunnel.Id == item.Id);
        if (current is not null) { item.Update(current, _state); }
        item.RefreshTime(_clock.GetUtcNow());
    }

    private Task OnUiAsync(Action action)
    {
        if (_disposed) { return Task.CompletedTask; }
        if (_dispatcher.HasThreadAccess) { action(); return Task.CompletedTask; }
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(() =>
        {
            try { if (!_disposed) { action(); } completion.TrySetResult(); }
            catch (Exception error) { completion.TrySetException(error); }
        })) { completion.TrySetCanceled(); }
        return completion.Task;
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken)
    {
        Task[] pending;
        lock (_commandSync) { _closing = true; pending = _commands.ToArray(); }
        await OnUiAsync(() => { Quick.Disable(); OnPropertyChanged(nameof(CanStopAll)); }).ConfigureAwait(false);
        await _stopping.CancelAsync().ConfigureAwait(false);
        await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        _disposed = true;
        _tunnels.StateChanged -= OnRuntimeChanged;
        _configuration.StateChanged -= OnConfigurationChanged;
        _activity.ErrorChanged -= OnActivityErrorChanged;
        _stopping.Cancel();
    }
}

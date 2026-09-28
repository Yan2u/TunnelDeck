using System.Collections.ObjectModel;
using System.Globalization;
using Microsoft.UI.Dispatching;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Navigation;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Profiles;
using TunnelDeck.Core.Tunnels;
using TunnelDeck.Core.Transfer;
using TunnelDeck.Core.Validation;
using TunnelDeck.Design;

namespace TunnelDeck.ViewModels;

public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private readonly DispatcherQueue _dispatcher;
    private AppState _state = new();
    private bool _ready;
    private bool _isBusy;
    private bool _disposed;
    private string _error = string.Empty;
    private int _themeIndex;

    public ShellViewModel(AppServices services, DispatcherQueue dispatcher)
    {
        _services = services;
        _dispatcher = dispatcher;
        services.Configuration.StateChanged += OnConfigurationChanged;
        services.Connections.ServerUsageChanged += OnServerUsageChanged;
        services.Forwarding.RuntimeChanged += OnRuntimeChanged;
        services.Profiles.StateChanged += OnProfileRunChanged;
    }

    public NavigationHistory Navigation { get; } = new();
    public TunnelWorkspace Forwarding => _services.Forwarding;
    public QuickForwardModel Quick => Forwarding.Quick;
    public AppState State => _state;
    public bool IsPreview => _services.PreviewScenario is not null;
    public IntPtr WindowHandle => _services.WindowHandle;
    public bool IsServerInUse(Guid serverId) => _services.Connections.IsServerInUse(serverId);
    public bool IsReady => _ready;
    public bool IsBusy { get => _isBusy; private set { if (SetProperty(ref _isBusy, value)) { OnPropertyChanged(nameof(CanEdit)); OnPropertyChanged(nameof(CanOpenTerminal)); } } }
    public bool CanEdit => _ready && !_services.Configuration.IsReadOnly && !IsBusy;
    public bool CanGoBack => Navigation.CanGoBack;
    public string Error { get => UiText.Translate(_error); private set { if (SetProperty(ref _error, value)) { OnPropertyChanged(nameof(HasError)); } } }
    public bool HasError => !string.IsNullOrEmpty(Error);
    public IReadOnlyList<ResourceRow> ServerChoices => _serverChoices;
    public ReadOnlyObservableCollection<TunnelItem> Tunnels => Forwarding.Items;
    public TunnelManagerSnapshot TunnelSnapshot => IsPreview ? new(0, [.. Tunnels.Select(item => item.Value)]) : _services.Tunnels.Current;
    public ProfileRunSnapshot? ProfileRun(Guid id) => _services.Profiles.Get(id);
    public string RuntimeStatus => Forwarding.RuntimeStatus;
    public string CompactRuntimeStatus
    {
        get
        {
            int count = Tunnels.Count(tunnel => tunnel.Value.IsRunning);
            return count > 99 ? "99+" : count.ToString(CultureInfo.CurrentCulture);
        }
    }
    public int ThemeIndex { get => _themeIndex; set => SetProperty(ref _themeIndex, value); }
    public event Action? StateUpdated;
    public event Action? TunnelsUpdated;
    public event Action<Guid>? ProfileRunUpdated;
    public event Action<Guid>? ServerUsageChanged;
    public event Action<AppRoute>? NavigationRequested;

    public void SetLoadResult(StateLoadResult result)
    {
        _ready = result.State is not null && !result.IsReadOnly;
        Forwarding.SetAvailable(_ready);
        if (result.State is not null) { ApplyState(result.State); }
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanOpenTerminal));
        StateUpdated?.Invoke();
    }

    public void SetLoadFailed()
    {
        _ready = false;
        Forwarding.SetAvailable(false);
        OnPropertyChanged(nameof(IsReady));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanOpenTerminal));
    }

    private void OnConfigurationChanged(AppState state) => _dispatcher.TryEnqueue(() => { if (!_disposed) { ApplyState(state); } });
    private void OnServerUsageChanged(Guid serverId) => _dispatcher.TryEnqueue(() => { if (!_disposed) { ServerUsageChanged?.Invoke(serverId); } });
    private void OnRuntimeChanged()
    {
        if (_disposed) { return; }
        OnPropertyChanged(nameof(RuntimeStatus));
        OnPropertyChanged(nameof(CompactRuntimeStatus));
        TunnelsUpdated?.Invoke();
    }
    private void OnProfileRunChanged(ProfileRunSnapshot snapshot) => _dispatcher.TryEnqueue(() => { if (!_disposed) { ProfileRunUpdated?.Invoke(snapshot.ProfileId); } });

    public void ApplyState(AppState state)
    {
        if (ReferenceEquals(_state, state) || state.Revision < _state.Revision) { return; }
        bool resourcesChanged = RefreshResourceCache(state);
        _state = state;
        UiText.SetLanguage(state.Settings.Language);
        bool changedRoute = Navigation.Reconcile(state);
        OnPropertyChanged(nameof(State));
        if (resourcesChanged) { OnPropertyChanged(nameof(ServerChoices)); }
        OnPropertyChanged(nameof(CanGoBack));
        StateUpdated?.Invoke();
        if (changedRoute) { NavigationRequested?.Invoke(Navigation.Current); }
    }

    public void Navigate(AppRoute route)
    {
        if (!route.ExistsIn(State)) { route = route.Parent; }
        if (Navigation.Navigate(route)) { AnnounceNavigation(); }
    }

    public override void RefreshLanguage()
    {
        if (_disposed) { return; }
        RefreshResourceCache(_state, force: true);
        base.RefreshLanguage();
        StateUpdated?.Invoke();
    }

    public void ReplaceRoute(AppRoute route) { if (Navigation.Replace(route)) { AnnounceNavigation(); } }
    public void ReplaceRouteIfCurrent(AppRoute expected, AppRoute replacement) { if (Navigation.ReplaceIfCurrent(expected, replacement)) { AnnounceNavigation(); } }
    public void GoBack() { if (Navigation.GoBack()) { AnnounceNavigation(); } }
    private void AnnounceNavigation() { OnPropertyChanged(nameof(CanGoBack)); NavigationRequested?.Invoke(Navigation.Current); }

    public Task<bool> TogglePinAsync(ResourceRow resource) => SaveAsync(() => _services.Configuration.UpdateAsync(state => state with
    {
        Order = resource.IsPinned ? ResourceOrderService.Unpin(state.Order, resource.Kind, resource.Id) : ResourceOrderService.Pin(state.Order, resource.Kind, resource.Id),
    }));

    public Task<bool> MovePinnedAsync(ResourceRow resource, int delta) => SaveAsync(() => _services.Configuration.UpdateAsync(state =>
    {
        var ids = resource.Kind == ResourceKind.Profile ? state.Order.PinnedProfileIds : state.Order.PinnedServerIds;
        int index = ids.IndexOf(resource.Id);
        int target = index + delta;
        return index < 0 || target < 0 || target >= ids.Length ? state : state with { Order = ResourceOrderService.MovePinned(state.Order, resource.Kind, index, target) };
    }));

    public Task<bool> SaveSettingsAsync(Func<AppSettings, AppSettings> update) => SaveAsync(() =>
        _services.Configuration.UpdateSettingsAsync(update));

    public Task<bool> ReorderPinnedAsync(ResourceKind kind, IEnumerable<Guid> expected, IEnumerable<Guid> reordered) =>
        SaveAsync(() => _services.Configuration.ReorderPinnedAsync(kind, expected, reordered));

    public Task<bool> SaveServerAsync(RemoteServer server, long revision, CredentialChange change) =>
        SaveAsync(() => _services.Connections.SaveServerAsync(server, change, revision));

    public Task<ConnectionTestResult> TestConnectionAsync(RemoteServer server, ConnectionCredentials? credentials,
        IProgress<ConnectionStage> progress, CancellationToken cancellationToken) =>
        _services.Connections.TestAsync(server, credentials, progress, cancellationToken);

    public Task<bool> SaveProfileAsync(ForwardProfile profile, ForwardProfile? original) => SaveAsync(() => _services.Configuration.SaveProfileAsync(profile, original));
    public Task<ProfileRunSnapshot> StartProfileAsync(Guid profileId) => _services.Profiles.StartAllAsync(profileId);
    public Task<ProfileRunSnapshot> StartProfileRuleAsync(Guid profileId, Guid ruleId) => _services.Profiles.StartRuleAsync(profileId, ruleId);
    public Task<ProfileRunSnapshot> RetryProfileAsync(Guid profileId) => _services.Profiles.RetryFailedAsync(profileId);
    public Task CancelProfileStartAsync(Guid profileId) => _services.Profiles.CancelAsync(profileId);

    public async Task<bool> SelectPreviewAsync(DesignScenarioKind kind)
    {
        if (!IsPreview) { return false; }
        DesignScenario scenario = DesignScenarioFactory.Create(kind);
        bool saved = await SaveAsync(() => _services.Configuration.UpdateAsync(current => scenario.State with { Revision = current.Revision }));
        if (saved)
        {
            Forwarding.SetPreview(scenario.Tunnels);
            StateUpdated?.Invoke();
        }
        return saved;
    }

    public void ReportError(string message) { ClearDeletionRetry(); Error = message; }
    public void ClearError() { ClearDeletionRetry(); Error = string.Empty; }

    private async Task<bool> SaveAsync(Func<Task<ConfigurationCommitResult>> operation)
    {
        if (!CanEdit) { return false; }
        IsBusy = true;
        ClearError();
        try
        {
            var result = await operation();
            ApplyState(result.State);
            if (!result.Diagnostics.IsEmpty) { Error = string.Join(" ", result.Diagnostics.Select(issue => UiText.Translate(issue.Message))); }
            return true;
        }
        catch (DomainValidationException exception) { Error = exception.Message; }
        catch (ImportDocumentException exception) { Error = exception.Message; }
        catch (OperationCanceledException) { Error = UiText.Get("ShellViewModel_TheOperationWasCancelledBeforeSaving"); }
        catch (StateStoreException exception) { Error = exception.Message; }
        catch (ConnectionException exception) { Error = exception.Message; }
        catch (Exception) { Error = UiText.Get("ShellViewModel_TheChangeCouldNotBeSavedYourPrevious"); }
        finally { IsBusy = false; }
        return false;
    }

    public void Dispose()
    {
        _disposed = true;
        _services.Configuration.StateChanged -= OnConfigurationChanged;
        _services.Connections.ServerUsageChanged -= OnServerUsageChanged;
        _services.Forwarding.RuntimeChanged -= OnRuntimeChanged;
        _services.Profiles.StateChanged -= OnProfileRunChanged;
        _services.Profiles.Dispose();
        _services.Forwarding.Dispose();
    }
}

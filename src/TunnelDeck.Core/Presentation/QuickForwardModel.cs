using System.Collections.Immutable;
using TunnelDeck.Core.Drafts;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Snapshots;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Presentation;

/// <summary>Retains the user's raw port text; a NumberBox's previous numeric Value is never a submission source.</summary>
public sealed class QuickForwardModel : ObservableModel
{
    private Guid? _serverId;
    private string _port = string.Empty, _localPort = string.Empty, _remotePort = string.Empty;
    private string _localHost = ForwardRule.DefaultHost, _remoteHost = ForwardRule.DefaultHost;
    private int _mode;
    private bool _auto = true, _advanced, _initialized, _enabled, _busy, _advancedInitialized;
    private ImmutableArray<ValidationIssue> _issues = [];
    private AppState _state = new();

    public Guid? SelectedServerId { get => _serverId; set { if (SetProperty(ref _serverId, value)) { ClearErrors(); AvailabilityChanged(); } } }
    public string PortText { get => _port; set { if (SetProperty(ref _port, value)) { ClearErrors(); AvailabilityChanged(); } } }
    public string LocalPortText { get => _localPort; set { if (SetProperty(ref _localPort, value)) { ClearErrors(); AvailabilityChanged(); } } }
    public string RemotePortText { get => _remotePort; set { if (SetProperty(ref _remotePort, value)) { ClearErrors(); AvailabilityChanged(); } } }
    public string LocalHost { get => _localHost; set { if (SetProperty(ref _localHost, value)) { ClearErrors(); } } }
    public string RemoteHost { get => _remoteHost; set { if (SetProperty(ref _remoteHost, value)) { ClearErrors(); } } }
    public bool AutoFindPort { get => _auto; set => SetProperty(ref _auto, value); }
    public bool IsLocal => ModeIndex == 0;
    public int ModeIndex
    {
        get => _mode;
        set
        {
            if (!SetProperty(ref _mode, value)) { return; }
            ClearErrors();
            foreach (string name in new[] { nameof(IsLocal), nameof(Direction), nameof(LocalHostLabel), nameof(RemoteHostLabel) }) { OnPropertyChanged(name); }
        }
    }
    public bool UseAdvanced
    {
        get => _advanced;
        set
        {
            if (!SetProperty(ref _advanced, value)) { return; }
            if (value && !_advancedInitialized) { LocalPortText = RemotePortText = PortText; _advancedInitialized = true; }
            ClearErrors(); OnPropertyChanged(nameof(UseSamePort)); AvailabilityChanged();
        }
    }
    public bool UseSamePort => !UseAdvanced;
    public bool IsBusy { get => _busy; internal set { if (SetProperty(ref _busy, value)) { AvailabilityChanged(); } } }
    public bool CanStart => _enabled && !IsBusy && _state.Servers.Any(server => server.Id == SelectedServerId) &&
        (UseAdvanced ? !string.IsNullOrWhiteSpace(LocalPortText) && !string.IsNullOrWhiteSpace(RemotePortText) : !string.IsNullOrWhiteSpace(PortText));
    public string Direction => IsLocal ? "Listen on this computer → connect from the SSH server to the target." : "Listen on the SSH server → connect from this computer to the target.";
    public string LocalHostLabel => IsLocal ? "Local listen address" : "Local target host";
    public string RemoteHostLabel => IsLocal ? "Remote target host" : "Remote listen address";
    public string PortError => UseAdvanced ? string.Empty : Issue("LocalPort");
    public string LocalPortError => UseAdvanced ? Issue("LocalPort") : string.Empty;
    public string RemotePortError => UseAdvanced ? Issue("RemotePort") : string.Empty;
    public string LocalHostError => Issue("LocalHost");
    public string RemoteHostError => Issue("RemoteHost");
    public string ServerError => Issue("ServerId").Length == 0 ? string.Empty : "Select an SSH server.";
    public bool HasErrors => !_issues.IsEmpty;

    public void Reconcile(AppState state, bool preview, bool enabled)
    {
        _state = state; _enabled = enabled && !preview;
        if (!_initialized && enabled)
        {
            AutoFindPort = state.Settings.AutoFindAvailablePortDefault;
            if (preview) { PortText = "3000"; }
            _initialized = true;
        }
        if (!state.Servers.Any(server => server.Id == SelectedServerId))
        {
            SelectedServerId = new[] { state.Settings.PinnedQuickServerId, state.Settings.DefaultServerId }
                .FirstOrDefault(id => id is not null && state.Servers.Any(server => server.Id == id));
        }
        AvailabilityChanged();
    }

    public RuleLaunchSnapshot? Capture()
    {
        var draft = new ForwardRuleDraft
        {
            ServerId = SelectedServerId, Mode = (ForwardMode)ModeIndex, AutoFindAvailablePort = AutoFindPort,
            LocalHost = UseAdvanced ? LocalHost : ForwardRule.DefaultHost, RemoteHost = UseAdvanced ? RemoteHost : ForwardRule.DefaultHost,
            LocalPortText = UseAdvanced ? LocalPortText : PortText, RemotePortText = UseAdvanced ? RemotePortText : PortText,
        };
        try
        {
            ForwardRule rule = draft.Build(_state.Servers.Select(server => server.Id).ToHashSet());
            RemoteServer server = _state.Servers.Single(server => server.Id == rule.ServerId);
            ClearErrors();
            return new(server with { }, rule, new() { ServerName = ResourceDisplay.ServerName(server), RuleId = rule.Id });
        }
        catch (DomainValidationException error) { _issues = error.Result.Issues; ErrorsChanged(); return null; }
    }

    internal void Disable() { _enabled = false; AvailabilityChanged(); }
    private string Issue(string path) => _issues.FirstOrDefault(issue => issue.Path.EndsWith(path, StringComparison.OrdinalIgnoreCase))?.Message ?? string.Empty;
    private void ClearErrors() { if (_issues.IsEmpty) { return; } _issues = []; ErrorsChanged(); }
    private void ErrorsChanged()
    {
        foreach (string name in new[] { nameof(PortError), nameof(LocalPortError), nameof(RemotePortError), nameof(LocalHostError), nameof(RemoteHostError), nameof(ServerError), nameof(HasErrors) }) { OnPropertyChanged(name); }
    }
    private void AvailabilityChanged() => OnPropertyChanged(nameof(CanStart));
}

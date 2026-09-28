using System.Collections.Immutable;
using TunnelDeck.Core.Drafts;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Presentation;

public sealed class RuleEditorModel : ObservableModel
{
    private Guid _id;
    private Guid? _serverId;
    private int _mode;
    private string _localHost = ForwardRule.DefaultHost, _remoteHost = ForwardRule.DefaultHost;
    private string _localPort = string.Empty, _remotePort = string.Empty;
    private bool _auto = true;
    private ImmutableArray<ValidationIssue> _issues = [];
    public Guid? SelectedServerId { get => _serverId; set { if (SetProperty(ref _serverId, value)) { ClearErrors(); } } }
    public int ModeIndex
    {
        get => _mode;
        set { if (SetProperty(ref _mode, value)) { ClearErrors(); OnPropertyChanged(nameof(IsLocal)); OnPropertyChanged(nameof(Direction)); OnPropertyChanged(nameof(LocalHostLabel)); OnPropertyChanged(nameof(RemoteHostLabel)); } }
    }
    public bool IsLocal => ModeIndex == 0;
    public string LocalHost { get => _localHost; set { if (SetProperty(ref _localHost, value)) { ClearErrors(); } } }
    public string RemoteHost { get => _remoteHost; set { if (SetProperty(ref _remoteHost, value)) { ClearErrors(); } } }
    public string LocalPortText { get => _localPort; set { if (SetProperty(ref _localPort, value)) { ClearErrors(); } } }
    public string RemotePortText { get => _remotePort; set { if (SetProperty(ref _remotePort, value)) { ClearErrors(); } } }
    public bool AutoFindPort { get => _auto; set => SetProperty(ref _auto, value); }
    public string Direction => IsLocal ? UiText.Get("QuickForwardModel_ListenOnThisComputerConnectFromTheSSH") : UiText.Get("QuickForwardModel_ListenOnTheSSHServerConnectFromThis");
    public string LocalHostLabel => IsLocal ? UiText.Get("QuickForwardModel_LocalListenAddress") : UiText.Get("QuickForwardModel_LocalTargetHost");
    public string RemoteHostLabel => IsLocal ? UiText.Get("QuickForwardModel_RemoteTargetHost") : UiText.Get("QuickForwardModel_RemoteListenAddress");
    public string ServerError => Issue("ServerId").Length == 0 ? string.Empty : UiText.Get("QuickForwardModel_SelectAnSSHServer");
    public string LocalPortError => Issue("LocalPort");
    public string RemotePortError => Issue("RemotePort");
    public string LocalHostError => Issue("LocalHost");
    public string RemoteHostError => Issue("RemoteHost");

    public void Load(ForwardRuleDraft draft)
    {
        _id = draft.Id; SelectedServerId = draft.ServerId; ModeIndex = (int)draft.Mode;
        LocalHost = draft.LocalHost; RemoteHost = draft.RemoteHost;
        LocalPortText = draft.LocalPortText; RemotePortText = draft.RemotePortText; AutoFindPort = draft.AutoFindAvailablePort;
        ClearErrors();
    }

    public ForwardRule? Capture(AppState state)
    {
        try
        {
            ForwardRule rule = new ForwardRuleDraft
            {
                Id = _id, ServerId = SelectedServerId, Mode = (ForwardMode)ModeIndex, AutoFindAvailablePort = AutoFindPort,
                LocalHost = LocalHost, RemoteHost = RemoteHost, LocalPortText = LocalPortText, RemotePortText = RemotePortText,
            }.Build(state.Servers.Select(server => server.Id).ToHashSet());
            ClearErrors(); return rule;
        }
        catch (DomainValidationException error) { _issues = error.Result.Issues; ErrorsChanged(); return null; }
    }

    private string Issue(string path) => UiText.Translate(_issues.FirstOrDefault(issue => issue.Path.EndsWith(path, StringComparison.OrdinalIgnoreCase))?.Message);
    private void ClearErrors() { if (_issues.IsEmpty) { return; } _issues = []; ErrorsChanged(); }
    private void ErrorsChanged()
    {
        foreach (string name in new[] { nameof(ServerError), nameof(LocalPortError), nameof(RemotePortError), nameof(LocalHostError), nameof(RemoteHostError) }) { OnPropertyChanged(name); }
    }
}

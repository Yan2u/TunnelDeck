using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json.Serialization;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Drafts;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Navigation;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.ViewModels;

public sealed partial class ServerDetailViewModel : ObservableObject
{
    private ShellViewModel _shell = null!;
    private RemoteServer? _original;
    private long _revision;
    private Guid _id;
    private bool _active;
    private string _title = UiText.Get("ServerDetailViewModel_NewServer");
    private string _displayName = string.Empty;
    private string _host = string.Empty;
    private string _username = string.Empty;
    private string _portText = "22";
    private string _identityFilePath = string.Empty;
    private string _secret = string.Empty;
    private string _credentialError = string.Empty;
    private string _keyFileError = string.Empty;
    private int _authenticationIndex;
    private bool _rememberCredential;
    private bool _savedCredentialMissing;
    private ImmutableArray<ValidationIssue> _issues = [];

    public string Title { get => _original is null ? UiText.Get("ServerDetailViewModel_NewServer") : _title; private set => SetProperty(ref _title, value); }
    public string DisplayName { get => _displayName; set => SetProperty(ref _displayName, value); }
    public string Host { get => _host; set { if (SetProperty(ref _host, value)) { OnIdentityInputChanged(); } } }
    public string Username { get => _username; set { if (SetProperty(ref _username, value)) { OnIdentityInputChanged(); } } }
    public string PortText { get => _portText; set { if (SetProperty(ref _portText, value)) { OnIdentityInputChanged(); } } }
    public string IdentityFilePath { get => _identityFilePath; set { if (SetProperty(ref _identityFilePath, value)) { OnIdentityInputChanged(); } } }
    public int AuthenticationIndex
    {
        get => _authenticationIndex;
        set
        {
            if (!SetProperty(ref _authenticationIndex, value)) { return; }
            Secret = string.Empty;
            RememberCredential = false;
            OnPropertyChanged(nameof(IsPrivateKey));
            OnPropertyChanged(nameof(SecretLabel));
            OnIdentityInputChanged();
        }
    }
    [JsonIgnore] public string Secret { get => _secret; set { if (SetProperty(ref _secret, value)) { ClearTestResult(); } } }
    public bool RememberCredential
    {
        get => _rememberCredential;
        set { if (SetProperty(ref _rememberCredential, value)) { OnPropertyChanged(nameof(SecretHint)); ClearTestResult(); } }
    }
    public bool IsPrivateKey => AuthenticationIndex == 1;
    public string SecretLabel => IsPrivateKey ? UiText.Get("DialogServiceCredentials_PrivateKeyPassphrase") : UiText.Get("ServerDetailPage_Password");
    public bool IsActive => _active;
    public bool IsPreview => _shell is not null && _shell.IsPreview;
    public bool CanSave => _active && _shell.CanEdit && !IsTesting;
    public bool CanDelete => _active && _original is not null && _shell.CanEdit;
    public bool CanOpenTerminal => _active && _shell.CanOpenTerminal && !IsTesting && _original is not null &&
        Host.Trim() == _original.Host && Username.Trim() == _original.Username &&
        PortText.Trim() == _original.Port.ToString(CultureInfo.InvariantCulture) &&
        AuthenticationIndex == (int)_original.AuthMethod && (!IsPrivateKey || IdentityFilePath.Trim() == _original.IdentityFilePath);
    public Task OpenTerminalAsync() => CanOpenTerminal ? _shell.OpenTerminalAsync(_id) : Task.CompletedTask;
    public Task DeleteAsync() => CanDelete ? _shell.DeleteResourceAsync(ResourceKind.Server, _id) : Task.CompletedTask;
    public bool CanEditName => _active && _shell.CanEdit;
    public bool IsServerInUse => _shell is not null && _shell.IsServerInUse(_id);
    public bool CanEditIdentity => CanSave && !IsServerInUse;
    public bool CanTestAction => _active && (IsTesting || (_shell.CanEdit && !IsPreview));
    public string DisplayNameError => ErrorFor(nameof(RemoteServer.DisplayName));
    public string HostError => ErrorFor(nameof(RemoteServer.Host));
    public string UsernameError => ErrorFor(nameof(RemoteServer.Username));
    public string PortError => ErrorFor(nameof(RemoteServer.Port));
    public string KeyPathError => _keyFileError.Length > 0 ? UiText.Translate(_keyFileError) : ErrorFor(nameof(RemoteServer.IdentityFilePath));
    public string CredentialError => UiText.Translate(_credentialError);
    public string SecretHint => _savedCredentialMissing
        ? UiText.Get("ServerDetailViewModel_TheSavedCredentialIsUnavailableEnterItAgain")
        : RememberCredential && CanUseSavedCredential
        ? UiText.Get("ServerDetailViewModel_ASavedIsAvailableLeaveThisFieldBlank", UiText.Get(IsPrivateKey ? "Common_Passphrase" : "Common_Password"))
        : IsPrivateKey ? UiText.Get("ServerDetailViewModel_LeaveBlankForAnUnencryptedKeyPassphrasesAre")
        : UiText.Get("ServerDetailViewModel_EnterThePasswordToTestThisConnectionSaving");

    private bool CanUseSavedCredential
    {
        get
        {
            if (_savedCredentialMissing || _original?.CredentialReference is null || !HostAddress.IsValidHost(Host.Trim()) ||
                !int.TryParse(PortText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int port)) { return false; }
            return ServerIdentity.SameAuthenticationTarget(_original, _original with
            {
                Host = Host.Trim(), Port = port, Username = Username.Trim(), AuthMethod = (AuthenticationMethod)AuthenticationIndex,
                IdentityFilePath = IsPrivateKey ? IdentityFilePath.Trim() : null,
            });
        }
    }

    public void Load(ShellViewModel shell, Guid? id)
    {
        _shell = shell;
        _shell.ServerUsageChanged += OnServerUsageChanged;
        _active = true;
        _original = shell.State.Servers.FirstOrDefault(server => server.Id == id);
        _id = _original?.Id ?? Guid.NewGuid();
        _revision = shell.State.Revision;
        _title = _original is null ? UiText.Get("ServerDetailViewModel_NewServer") : ResourceDisplay.ServerName(_original);
        _displayName = _original?.DisplayName ?? string.Empty;
        _host = _original?.Host ?? string.Empty;
        _username = _original?.Username ?? string.Empty;
        _portText = (_original?.Port ?? 22).ToString(CultureInfo.InvariantCulture);
        ServerDraft draft = _original is null
            ? ServerDefaultsResolver.CreateDraft(shell.State.Settings, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), File.Exists)
            : ServerDraft.FromServer(_original);
        _authenticationIndex = (int)draft.AuthMethod;
        _identityFilePath = draft.IdentityFilePath ?? string.Empty;
        _rememberCredential = _original?.CredentialReference is not null;
    }

    public async Task SaveAsync()
    {
        if (!CanSave) { return; }
        AppRoute editingRoute = AppRoute.Server(_original?.Id);
        RemoteServer? candidate = BuildCandidate();
        if (candidate is null) { return; }
        CredentialChange change = CredentialChange.Forget;
        if (RememberCredential)
        {
            if (Secret.Length > 0) { change = CredentialChange.Remember(Secret); }
            else if (CanUseSavedCredential) { change = CredentialChange.Keep; }
            else
            {
                _credentialError = UiText.Get("ServerDetailViewModel_EnterAToRememberOrClearRememberTo", SecretLabel.ToLowerInvariant());
                RefreshFieldErrors();
                return;
            }
        }
        if (await _shell.SaveServerAsync(candidate, _revision, change))
        {
            _original = _shell.State.Servers.First(server => server.Id == _id);
            _revision = _shell.State.Revision;
            Title = ResourceDisplay.ServerName(_original);
            _savedCredentialMissing = false;
            if (change.Kind == CredentialChangeKind.Remember) { Secret = string.Empty; }
            OnPropertyChanged(nameof(SecretHint));
            RefreshAvailability();
            _shell.ReplaceRouteIfCurrent(editingRoute, AppRoute.Server(_id));
        }
    }

    private RemoteServer? BuildCandidate()
    {
        _issues = [];
        _credentialError = string.Empty;
        _keyFileError = string.Empty;
        try
        {
            return new ServerDraft
            {
                Id = _id, DisplayName = DisplayName, Host = Host, Username = Username, PortText = PortText,
                AuthMethod = (AuthenticationMethod)AuthenticationIndex, IdentityFilePath = IsPrivateKey ? IdentityFilePath : null,
                CredentialReference = _original?.CredentialReference, LastUsedAt = _original?.LastUsedAt,
            }.Build();
        }
        catch (DomainValidationException exception) { _issues = exception.Result.Issues; return null; }
        finally { RefreshFieldErrors(); }
    }

    private void OnIdentityInputChanged() { OnPropertyChanged(nameof(SecretHint)); OnPropertyChanged(nameof(CanOpenTerminal)); ClearTestResult(); }
    private string ErrorFor(string field) => string.Join(" ", _issues.Where(issue => issue.Path == field).Select(issue => UiText.Translate(issue.Message)));
    private void RefreshFieldErrors()
    {
        foreach (string property in new[] { nameof(DisplayNameError), nameof(HostError), nameof(UsernameError), nameof(PortError), nameof(KeyPathError), nameof(CredentialError) })
        {
            OnPropertyChanged(property);
        }
    }

    public void RefreshAvailability()
    {
        foreach (string property in new[] { nameof(CanSave), nameof(CanDelete), nameof(CanOpenTerminal), nameof(CanEditName), nameof(CanEditIdentity), nameof(IsServerInUse), nameof(CanTestAction) })
        {
            OnPropertyChanged(property);
        }
    }

    private void OnServerUsageChanged(Guid serverId) { if (_active && serverId == _id) { RefreshAvailability(); } }

    public void ReportKeyPickerError() { _keyFileError = UiText.Get("ServerDetailViewModel_TheFilePickerCouldNotBeOpenedEnter"); RefreshFieldErrors(); }

    public void Unload()
    {
        _active = false;
        _shell.ServerUsageChanged -= OnServerUsageChanged;
        _testingCancellation?.Cancel();
        Secret = string.Empty;
    }
}

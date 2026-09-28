using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;

namespace TunnelDeck.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private ShellViewModel _shell = null!;
    private Func<AppSettings, AppSettings>? _retry;
    private string _error = string.Empty;
    private bool _active, _keyPathDirty;
    private string _defaultIdentityFilePath = string.Empty;
    private string? _detectedKeyPath;
    public string Error { get => UiText.Translate(_error); private set => SetProperty(ref _error, value); }
    public bool CanEdit => _active && _shell.CanEdit;
    public bool CanRetry => CanEdit && _retry is not null;
    public bool IsActive => _active;
    public int LanguageIndex => (_shell?.State.Settings.Language ?? UiText.English) == UiText.SimplifiedChinese ? 1 : 0;
    public int DefaultAuthenticationIndex => (int)(_shell?.State.Settings.DefaultAuthenticationMethod ?? AuthenticationMethod.Password);
    public bool UsesPrivateKey => DefaultAuthenticationIndex == (int)AuthenticationMethod.PrivateKey;
    public string DefaultIdentityFilePath
    {
        get => _defaultIdentityFilePath;
        set { if (SetProperty(ref _defaultIdentityFilePath, value)) { _keyPathDirty = true; RefreshAvailability(); } }
    }
    public bool CanSaveKeyPath => CanEdit && UsesPrivateKey && _keyPathDirty;
    public string KeyPathHint => _keyPathDirty ? UiText.Get("SettingsViewModel_PressEnterOrSavePathToSaveThis")
        : DefaultIdentityFilePath.Length > 0 ? UiText.Get("SettingsViewModel_UsedForNewServersAndSSHConfigEntries")
        : UiText.Get("SettingsViewModel_NoIdRsaFileWasFoundInYour");

    public void Load(ShellViewModel shell)
    {
        _shell = shell;
        _active = true;
        _detectedKeyPath = ServerDefaultsResolver.DetectIdentityFile(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), File.Exists);
        ReconcileAuthentication();
    }
    public void Unload() { _active = false; _retry = null; }
    public void RefreshAvailability()
    {
        foreach (string name in new[] { nameof(CanEdit), nameof(CanRetry), nameof(CanSaveKeyPath), nameof(KeyPathHint) }) { OnPropertyChanged(name); }
    }

    public void ReconcileAuthentication()
    {
        string? savedPath = _shell.State.Settings.DefaultIdentityFilePath;
        if (_keyPathDirty && string.Equals(DefaultIdentityFilePath.Trim(), savedPath ?? string.Empty, StringComparison.OrdinalIgnoreCase)) { _keyPathDirty = false; }
        if (!_keyPathDirty) { SetProperty(ref _defaultIdentityFilePath, savedPath ?? _detectedKeyPath ?? string.Empty, nameof(DefaultIdentityFilePath)); }
        OnPropertyChanged(nameof(DefaultAuthenticationIndex));
        OnPropertyChanged(nameof(UsesPrivateKey));
        RefreshAvailability();
    }

    public Task SetDefaultAuthenticationAsync(int index)
    {
        if (!CanEdit || !Enum.IsDefined((AuthenticationMethod)index)) { return Task.CompletedTask; }
        AuthenticationMethod method = (AuthenticationMethod)index;
        return SaveAsync(settings => settings with
        {
            DefaultAuthenticationMethod = method,
            DefaultIdentityFilePath = method == AuthenticationMethod.PrivateKey ? settings.DefaultIdentityFilePath ?? _detectedKeyPath : settings.DefaultIdentityFilePath,
        });
    }

    public async Task SaveDefaultKeyAsync()
    {
        if (!CanSaveKeyPath) { return; }
        string input = DefaultIdentityFilePath.Trim();
        string? path = null;
        if (input.Length > 0)
        {
            try
            {
                if (!Path.IsPathFullyQualified(input) || input.Any(char.IsControl)) { throw new ArgumentException(); }
                path = Path.GetFullPath(input);
                bool exists = await Task.Run(() => File.Exists(path));
                if (!CanEdit || DefaultIdentityFilePath.Trim() != input) { return; }
                if (!exists) { throw new ArgumentException(); }
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                _retry = null;
                Error = UiText.Get("SettingsViewModel_ChooseAnExistingPrivateKeyFileUsingIts");
                RefreshAvailability();
                return;
            }
        }
        DefaultIdentityFilePath = path ?? string.Empty;
        _detectedKeyPath = ServerDefaultsResolver.DetectIdentityFile(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), File.Exists);
        await SaveAsync(settings => settings with { DefaultIdentityFilePath = path });
    }

    public void ReportKeyPickerError() => Error = UiText.Get("SettingsViewModel_TheFilePickerCouldNotBeOpenedEnter");

    public Task SetMaximumAsync(double value)
    {
        if (!CanEdit) { return Task.CompletedTask; }
        if (!double.IsFinite(value) || value is < AppSettings.MinimumSidebarItems or > AppSettings.MaximumSidebarItems || value % 1 != 0)
        {
            _retry = null;
            Error = UiText.Get("SettingsViewModel_ChooseAWholeNumberFrom3To10");
            RefreshAvailability();
            return Task.CompletedTask;
        }
        return SaveAsync(settings => settings with { SidebarMaximumItems = (int)value });
    }

    public Task SetAutoFindAsync(bool value) => SaveAsync(settings => settings with { AutoFindAvailablePortDefault = value });
    public Task SetLanguageAsync(int index) => index is 0 or 1
        ? SaveAsync(settings => settings with { Language = index == 1 ? UiText.SimplifiedChinese : UiText.English })
        : Task.CompletedTask;
    public Task SetDefaultServerAsync(Guid? id) => SaveAsync(settings => settings with { DefaultServerId = id });
    public Task RetryAsync() => CanRetry ? SaveAsync(_retry!) : Task.CompletedTask;

    private async Task SaveAsync(Func<AppSettings, AppSettings> update)
    {
        if (!CanEdit) { return; }
        _retry = update;
        Error = string.Empty;
        bool saved = await _shell.SaveSettingsAsync(update);
        if (!_active) { return; }
        if (saved) { _retry = null; }
        else
        {
            Error = _shell.Error;
            _shell.ClearError();
        }
        RefreshAvailability();
    }
}

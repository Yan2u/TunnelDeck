using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Transfer;

namespace TunnelDeck.ViewModels;

public sealed class ExportViewModel : ObservableObject
{
    private ShellViewModel _shell = null!;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _active, _busy;
    private int _scope;
    private string _error = string.Empty, _savedPath = string.Empty;
    private TunnelDeckExport? _document;
    private IReadOnlyList<ImportItem> _items = [];

    public int ScopeIndex { get => _scope; set { if (SetProperty(ref _scope, value)) { SavedPath = string.Empty; Refresh(); } } }
    public string Error { get => UiText.Translate(_error); private set => SetProperty(ref _error, value); }
    public string SavedPath { get => _savedPath.Length == 0 ? string.Empty : UiText.Get("ExportViewModel_ExportSavedTo", _savedPath); private set => SetProperty(ref _savedPath, value); }
    public IReadOnlyList<ImportItem> Items => _items;
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); RefreshAvailability(); } }
    public bool CanChooseScope => _active && _shell.CanEdit && !IsBusy;
    public bool CanExport => CanChooseScope && _document is not null && _document.Servers.Length + _document.Profiles.Length > 0;
    public string Summary => _document is null ? string.Empty : $"{ResourceDisplay.Count(_document.Servers.Length, "server")} · {ResourceDisplay.Count(_document.Profiles.Length, "profile")}";

    public void Load(ShellViewModel shell, ExportScope scope) { _shell = shell; _scope = (int)scope; _active = true; Refresh(); }
    public void Unload() { _active = false; _lifetime.Cancel(); if (!IsBusy) { _lifetime.Dispose(); } }
    public void Refresh()
    {
        if (!_active || IsBusy) { return; }
        _document = ExportBuilder.Build(_shell.State, (ExportScope)ScopeIndex, DateTimeOffset.UtcNow);
        _items = _document.Servers.Select(server => new ImportItem(ResourceKind.Server,
            ResourceDisplay.ServerName(server.ToServer(server.Id)), $"{server.Username}@{server.Host}:{server.Port}", true, []))
            .Concat(_document.Profiles.Select(profile => new ImportItem(ResourceKind.Profile, profile.Name,
                UiText.Invariant("ExportViewModel_RequiredServersIncluded", UiText.Count(profile.Rules.Length, "rule", System.Globalization.CultureInfo.GetCultureInfo(UiText.English))), true, []))).ToArray();
        OnPropertyChanged(nameof(Items)); OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(ScopeIndex));
        RefreshAvailability();
    }

    public void RefreshAvailability() { OnPropertyChanged(nameof(CanChooseScope)); OnPropertyChanged(nameof(CanExport)); }

    public async Task ExportAsync(Func<Task<string?>> chooseFile)
    {
        if (!CanExport || _document is not TunnelDeckExport document) { return; }
        IsBusy = true; Error = string.Empty; SavedPath = string.Empty;
        try
        {
            string? path = await chooseFile();
            if (path is null || !_active || !_shell.CanEdit) { return; }
            await _shell.ExportAsync(path, document, _lifetime.Token);
            if (_active) { SavedPath = path; }
        }
        catch (OperationCanceledException) { }
        catch (ImportDocumentException exception) { if (_active) { Error = exception.Message; } }
        catch (Exception) { if (_active) { Error = UiText.Get("ExportViewModel_TheExportFileCouldNotBeSelectedOr"); } }
        finally { IsBusy = false; if (!_active) { _lifetime.Dispose(); } else { Refresh(); } }
    }
}

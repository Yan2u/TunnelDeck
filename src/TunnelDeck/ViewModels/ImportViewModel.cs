using TunnelDeck.Core.Navigation;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Transfer;

namespace TunnelDeck.ViewModels;

public sealed class ImportViewModel : ObservableObject
{
    private ShellViewModel _shell = null!;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _previewRequest;
    private bool _active, _importing, _previewPending, _skipUnavailable;
    private int _sourceIndex;
    private string _path = string.Empty, _error = string.Empty;
    private ImportPlan? _plan;
    private IReadOnlyList<ImportPreviewEntry> _items = [];

    public string FilePath { get => _path; set { if (SetProperty(ref _path, value)) { Invalidate(); } } }
    public int SourceIndex { get => _sourceIndex; set { if (value is 0 or 1 && SetProperty(ref _sourceIndex, value)) { Invalidate(); } } }
    public string Error { get => UiText.Translate(_error); private set => SetProperty(ref _error, value); }
    public ImportPlan? Plan => _plan;
    public IReadOnlyList<ImportPreviewEntry> Items => _items;
    public bool IsBusy => _importing || _previewRequest is not null;
    public string ProgressLabel => _importing ? UiText.Get("ImportViewModel_ImportingResources") : UiText.Get("ImportViewModel_ReadingImportFile");
    public bool CanChooseFile => _active && _shell.CanEdit && !_importing;
    public bool CanPreview => CanChooseFile && !string.IsNullOrWhiteSpace(FilePath);
    public bool CanRetryPreview => CanPreview && _plan is null && Error.Length > 0 && !IsBusy;
    public bool IsStale => _plan is not null && _shell.State.Revision != _plan.BaseRevision;
    public bool CanImport => CanChooseFile && !IsBusy && _plan is { CanCommit: true } && !IsStale && (!HasSkipped || SkipUnavailable);
    public bool HasSkipped => _plan is { SkippedCount: > 0 };
    public bool SkipUnavailable { get => _skipUnavailable; set { SetProperty(ref _skipUnavailable, value); RefreshAvailability(); } }
    public string SkipLabel => UiText.Get(_plan?.SkippedCount == 1 ? "Import_SkipOne" : "Import_SkipOther", _plan?.SkippedCount ?? 0);
    public string Summary => _plan is null ? string.Empty
        : UiText.Get("ImportViewModel_Skipped", ResourceDisplay.Count(_plan.Servers.Length, "new server"), ResourceDisplay.Count(_plan.Profiles.Length, "new profile"), _plan.SkippedCount);
    public string Warnings => IsStale ? UiText.Get("ImportViewModel_SavedConfigurationChangedUpdatingThePreview")
        : _plan is null ? string.Empty : string.Join("\n", _plan.Warnings.Select(message => UiText.Translate(message)));
    public string ImportLabel => HasSkipped && _plan is { CanCommit: true } ? UiText.Get("ImportViewModel_ImportAvailableEntries") : UiText.Get("ImportPage_Import");
    public string ActionHint => IsBusy ? string.Empty
        : !CanChooseFile ? UiText.Get("ImportViewModel_ImportIsUnavailableWhileSavedDataIsLoading")
        : _plan is null ? (Error.Length == 0 ? UiText.Get("ImportViewModel_ChooseAFileToSeeItsImportPreview") : string.Empty)
        : IsStale ? UiText.Get("ImportViewModel_UpdatingThePreview")
        : !_plan.CanCommit ? UiText.Get("ImportViewModel_NoEntriesCanBeImportedFixTheConnection")
        : HasSkipped && !SkipUnavailable ? UiText.Get("ImportViewModel_ConfirmThatTheUnavailableEntriesCanBeSkipped")
        : string.Empty;

    public void Load(ShellViewModel shell) { _shell = shell; _active = true; RefreshAvailability(); }
    public void Unload() { _active = false; CancelPreview(); _lifetime.Cancel(); _lifetime.Dispose(); }
    public void ReportPickerError() => Error = UiText.Get("ImportViewModel_TheFilePickerCouldNotBeOpenedEnter");

    public void SelectFile(string path)
    {
        if (!CanChooseFile) { return; }
        SetProperty(ref _path, path, nameof(FilePath));
        Invalidate();
    }

    public void ReloadPreview() { if (CanPreview) { Invalidate(); } }

    private async Task PreviewAsync(CancellationTokenSource request, string path, ImportSource source)
    {
        try
        {
            // Keep path editing responsive, and never publish a result for an older path or format.
            await Task.Delay(350, request.Token);
            ImportPlan plan = await _shell.PreviewImportAsync(path, source, request.Token);
            if (IsCurrent(request))
            {
                if (plan.BaseRevision != _shell.State.Revision) { Invalidate(); }
                else
                {
                    _plan = plan;
                    _items = plan.Items.Select((item, index) => new ImportPreviewEntry(item, index)).ToArray();
                    RefreshPlan();
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (ImportDocumentException exception) { if (IsCurrent(request)) { Error = exception.Message; } }
        catch (Exception) { if (IsCurrent(request)) { Error = UiText.Get("ImportViewModel_TheFileCouldNotBePreviewedCheckIts"); } }
        finally
        {
            if (ReferenceEquals(_previewRequest, request)) { _previewRequest = null; NotifyAvailability(); }
            request.Dispose();
        }
    }

    public async Task ImportAsync()
    {
        if (!CanImport || _plan is not ImportPlan plan) { return; }
        _importing = true; Error = string.Empty; NotifyAvailability();
        try
        {
            bool saved = await _shell.CommitImportAsync(plan, _lifetime.Token);
            if (saved && _active)
            {
                _plan = null;
                _shell.ReplaceRouteIfCurrent(AppRoute.Import, plan.Profiles.Length > 0 ? AppRoute.Profiles : AppRoute.Servers);
            }
            else if (!saved)
            {
                if (_active) { Error = _shell.Error; }
                _shell.ClearError();
            }
        }
        finally { _importing = false; RefreshAvailability(); }
    }

    public void RefreshAvailability()
    {
        if (_active && !_importing && IsStale) { Invalidate(); }
        else { StartPendingPreview(); NotifyAvailability(); }
    }

    private void NotifyAvailability()
    {
        foreach (string property in new[] { nameof(IsBusy), nameof(ProgressLabel), nameof(CanChooseFile), nameof(CanPreview), nameof(CanRetryPreview), nameof(CanImport), nameof(IsStale), nameof(Warnings), nameof(ActionHint) })
        { OnPropertyChanged(property); }
    }

    private bool IsCurrent(CancellationTokenSource request) => _active && !request.IsCancellationRequested && ReferenceEquals(_previewRequest, request);

    private void CancelPreview() { _previewRequest?.Cancel(); _previewRequest = null; }

    private void Invalidate()
    {
        CancelPreview();
        _plan = null; _items = []; Error = string.Empty; _skipUnavailable = false;
        _previewPending = !string.IsNullOrWhiteSpace(FilePath);
        StartPendingPreview();
        RefreshPlan();
    }

    private void StartPendingPreview()
    {
        if (!_previewPending || !CanChooseFile) { return; }
        _previewPending = false;
        _previewRequest = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _ = PreviewAsync(_previewRequest, FilePath.Trim(), (ImportSource)SourceIndex);
    }

    private void RefreshPlan()
    {
        foreach (string property in new[] { nameof(Plan), nameof(Items), nameof(Summary), nameof(HasSkipped), nameof(SkipUnavailable), nameof(SkipLabel), nameof(ImportLabel) })
        { OnPropertyChanged(property); }
        NotifyAvailability();
    }
}

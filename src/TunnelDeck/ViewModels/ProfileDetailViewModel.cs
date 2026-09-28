using System.ComponentModel;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Navigation;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Profiles;

namespace TunnelDeck.ViewModels;

public sealed class ProfileDetailViewModel : ObservableObject, IDisposable
{
    private ShellViewModel _shell = null!;
    private AppRoute _editingRoute = AppRoute.Profile();
    private ProfileRunSnapshot? _run;
    private bool _updating, _active;
    private string _operationError = string.Empty;
    public ProfileEditorModel Editor { get; private set; } = new(new(), null);
    public bool IsStarting => _run?.IsBusy == true;
    public bool CanStartAll => _active && Editor.CanRunSaved && !_shell.IsPreview && !IsStarting;
    public bool CanDelete => _active && Editor.Original is not null && _shell.CanEdit;
    public Task DeleteAsync() => CanDelete ? _shell.DeleteResourceAsync(ResourceKind.Profile, Editor.Id) : Task.CompletedTask;
    public bool CanRetryFailed => CanStartAll && _run is not null && Editor.Rules.Any(rule =>
        _run.Rules.Any(result => result.Launch.Requested.Id == rule.Id && result.Outcome == ProfileRuleOutcome.Failed));
    public string RunSummary => _run?.Summary ?? string.Empty;
    public string OperationError { get => UiText.Translate(_operationError); private set => SetProperty(ref _operationError, value); }

    public void Load(ShellViewModel shell, Guid? id)
    {
        _shell = shell; _editingRoute = AppRoute.Profile(id); _active = true;
        Editor = new(shell.State, id);
        Editor.PropertyChanged += OnDraftChanged;
        shell.TunnelsUpdated += RefreshRuntime;
        shell.ProfileRunUpdated += OnProfileRunUpdated;
        OnPropertyChanged(nameof(Editor));
        Update();
    }

    public void Update()
    {
        if (!_active) { return; }
        _updating = true;
        try
        {
            _run = _shell.ProfileRun(Editor.Id);
            Editor.UpdateState(_shell.State);
            Editor.SetAvailability(_shell.CanEdit && !IsStarting);
        }
        finally { _updating = false; }
        RefreshRows();
    }
    private void OnProfileRunUpdated(Guid id) { if (id == Editor.Id) { Update(); } }
    private void RefreshRuntime() { if (_active) { RefreshRows(); } }
    private void OnDraftChanged(object? sender, PropertyChangedEventArgs args)
    { if (!_updating && args.PropertyName == nameof(ProfileEditorModel.HasChanges)) { RefreshRows(); } }
    private void RefreshRows()
    {
        var runtime = _shell.TunnelSnapshot;
        for (int index = 0; index < Editor.Rules.Count; index++)
        {
            Editor.Rules[index].Refresh(_shell.State, runtime, _run, CanStartAll, !_shell.IsPreview && _shell.IsReady,
                Editor.CanEdit, index, Editor.Rules.Count);
        }
        foreach (string name in new[] { nameof(IsStarting), nameof(CanStartAll), nameof(CanRetryFailed), nameof(CanDelete), nameof(RunSummary) }) { OnPropertyChanged(name); }
    }

    public async Task SaveAsync()
    {
        if (!Editor.CanSave || Editor.Capture() is not { } candidate) { return; }
        bool saved = await _shell.SaveProfileAsync(candidate, Editor.Original);
        if (!_active || !saved) { return; }
        Editor.Saved(_shell.State.Profiles.Single(profile => profile.Id == candidate.Id));
        _shell.ReplaceRouteIfCurrent(_editingRoute, AppRoute.Profile(candidate.Id));
        _editingRoute = AppRoute.Profile(candidate.Id);
        Update();
    }

    public Task StartAllAsync() => !CanStartAll ? Task.CompletedTask : RunAsync(() => _shell.StartProfileAsync(Editor.Id));
    public Task RetryFailedAsync() => !CanRetryFailed ? Task.CompletedTask : RunAsync(() => _shell.RetryProfileAsync(Editor.Id));
    public Task StartRuleAsync(ProfileRuleItem item) => !item.CanStart ? Task.CompletedTask : RunAsync(() => _shell.StartProfileRuleAsync(Editor.Id, item.Id));
    public Task CancelStartAsync() => RunAsync(() => _shell.CancelProfileStartAsync(Editor.Id));
    public Task StopRuleAsync(ProfileRuleItem item) => item.CanStop && _shell.Tunnels.FirstOrDefault(tunnel => tunnel.Id == item.RuntimeId) is { } tunnel
        ? RunAsync(() => _shell.Forwarding.StopAsync(tunnel)) : Task.CompletedTask;

    private async Task RunAsync(Func<Task> action)
    {
        OperationError = string.Empty;
        try { Task work = action(); Update(); await work; }
        catch (Exception) { if (_active) { OperationError = UiText.Get("ProfileDetailViewModel_TheProfileOperationCouldNotFinishCheckIts"); } }
        finally { Update(); }
    }

    public void Dispose()
    {
        _active = false;
        Editor.PropertyChanged -= OnDraftChanged;
        _shell.TunnelsUpdated -= RefreshRuntime;
        _shell.ProfileRunUpdated -= OnProfileRunUpdated;
    }
}

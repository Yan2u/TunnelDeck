using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;

namespace TunnelDeck.ViewModels;

public sealed partial class ShellViewModel
{
    private Func<Task>? _retryDeletion;
    private bool _retryCredentialCleanup;
    private string _deletionStatus = string.Empty;
    public bool HasDeletionRetry => _retryDeletion is not null;
    public string DeletionRetryLabel => _retryCredentialCleanup ? UiText.Get("ShellViewModelDeletion_RetryCredentialCleanup") : UiText.Get("ShellViewModelDeletion_ReviewAndRetry");
    public bool IsDeleting => !string.IsNullOrEmpty(DeletionStatus);
    public string DeletionStatus
    {
        get => UiText.Translate(_deletionStatus);
        private set { if (SetProperty(ref _deletionStatus, value)) { OnPropertyChanged(nameof(IsDeleting)); } }
    }

    public Task RetryDeletionAsync() => _retryDeletion?.Invoke() ?? Task.CompletedTask;

    public async Task DeleteResourceAsync(ResourceKind kind, Guid? id = null)
    {
        if (!CanEdit) { return; }
        ClearError();
        IsBusy = true;
        try
        {
            DeletionImpact impact = id is Guid resourceId
                ? kind == ResourceKind.Server ? _services.Deletion.CalcServerDeletionImpact([resourceId]) : _services.Deletion.CalcProfileDeletionImpact([resourceId])
                : _services.Deletion.CalcAllDeletionImpact(kind);
            string update = string.Empty;
            while (impact.ResourceCount > 0)
            {
                if (!await _services.Dialogs.ConfirmDeletionAsync(impact, update)) { return; }
                DeletionStatus = kind == ResourceKind.Server ? UiText.Get("ShellViewModelDeletion_StoppingAffectedForwardsAndDeletingServers") : UiText.Get("ShellViewModelDeletion_DeletingProfiles");
                DeletionResult result = await _services.Deletion.DeleteConfirmedAsync(impact);
                DeletionStatus = string.Empty;
                if (_disposed) { return; }
                if (result.Outcome == DeletionOutcome.NeedsConfirmation) { impact = result.Impact; update = result.Message; continue; }
                if (result.Succeeded)
                {
                    if (result.State is not null) { ApplyState(result.State); }
                    if (!result.Diagnostics.IsDefaultOrEmpty)
                    {
                        Error = UiText.Get("ShellViewModelDeletion_DeletedSuccessfully") + string.Join(" ", result.Diagnostics.Select(issue => UiText.Translate(issue.Message)));
                        _retryDeletion = RetryDeletionCredentialCleanupAsync;
                        _retryCredentialCleanup = true;
                        OnPropertyChanged(nameof(DeletionRetryLabel));
                        OnPropertyChanged(nameof(HasDeletionRetry));
                    }
                }
                else
                {
                    Error = result.Message;
                    _retryDeletion = () => DeleteResourceAsync(kind, id);
                    OnPropertyChanged(nameof(HasDeletionRetry));
                }
                return;
            }
        }
        catch (OperationCanceledException) { /* Closing a confirmation has no side effects. */ }
        catch (Exception) { if (!_disposed) { Error = UiText.Get("ShellViewModelDeletion_TheDeletionCouldNotBeCompletedReviewThe"); } }
        finally { DeletionStatus = string.Empty; IsBusy = false; }
    }

    private async Task RetryDeletionCredentialCleanupAsync()
    {
        if (!CanEdit) { return; }
        IsBusy = true;
        try
        {
            var diagnostics = await _services.Configuration.RetryCredentialCleanupAsync();
            if (diagnostics.IsEmpty) { ClearError(); }
            else { Error = UiText.Get("ShellViewModelDeletion_TheResourcesWereDeletedSavedCredentialCleanupStill"); }
        }
        catch (Exception) { Error = UiText.Get("ShellViewModelDeletion_TheResourcesWereDeletedSavedCredentialCleanupStill"); }
        finally { IsBusy = false; }
    }

    private void ClearDeletionRetry()
    {
        _retryDeletion = null; _retryCredentialCleanup = false;
        OnPropertyChanged(nameof(HasDeletionRetry)); OnPropertyChanged(nameof(DeletionRetryLabel));
    }
}

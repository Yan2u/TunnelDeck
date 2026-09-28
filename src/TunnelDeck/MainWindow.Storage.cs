using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Design;

namespace TunnelDeck;

public sealed partial class MainWindow
{
    private bool _storageStarted;
    private bool _storageLoading;
    private bool _previewSeeded;

    private async void OnRootLoaded(object sender, RoutedEventArgs args)
    {
        if (_storageStarted) { return; }
        _storageStarted = true;
        await InitializeStorageAsync();
    }

    private async void OnStorageRetryClick(object sender, RoutedEventArgs args) => await InitializeStorageAsync();

    private async Task InitializeStorageAsync()
    {
        if (_storageLoading || _closing || _closed) { return; }
        _storageLoading = true;
        StorageRetry.IsEnabled = false;
        StorageNotice.IsOpen = true;
        StorageNotice.Severity = InfoBarSeverity.Informational;
        StorageNotice.Title = UiText.Get("MainWindow_LoadingSavedData");
        StorageNotice.Message = string.Empty;
        UpdateNoticeVisibility();
        try
        {
            StateLoadResult loaded = await _services.Configuration.InitializeAsync();
            if (_closed || _closing) { return; }
            Shell.SetLoadResult(loaded);
            if (!_previewSeeded && !loaded.IsReadOnly && _services.PreviewScenario is DesignScenarioKind scenario)
            {
                _previewSeeded = await Shell.SelectPreviewAsync(scenario);
            }
            StorageNotice.IsOpen = loaded.IsReadOnly || !loaded.Diagnostics.IsEmpty;
            StorageNotice.Severity = loaded.IsReadOnly ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
            StorageNotice.Title = loaded.IsReadOnly ? UiText.Get("MainWindowStorage_SavedDataIsReadOnly") : loaded.RecoveredFromBackup ? UiText.Get("MainWindowStorage_LoadedASavedBackup") : UiText.Get("MainWindowStorage_SavedDataNeedsAttention");
            StorageNotice.Message = string.Join(" ", loaded.Diagnostics.Select(issue => UiText.Translate(issue.Message)));
            StorageRetry.Visibility = StorageNotice.IsOpen ? Visibility.Visible : Visibility.Collapsed;
            if (loaded.IsReadOnly) { StorageNotice.Message += UiText.Get("MainWindowStorage_DataFolder", _services.DataDirectory.RootPath); }
        }
        catch (StateStoreException exception)
        {
            if (_closed || _closing) { return; }
            ShowStorageError(exception.Message);
        }
        catch (Exception)
        {
            if (_closed || _closing) { return; }
            ShowStorageError(UiText.Get("MainWindowStorage_CheckAccessToTheDataFolderThenRetry"));
        }
        finally { _storageLoading = false; if (!_closed) { StorageRetry.IsEnabled = true; UpdateNoticeVisibility(); } }
    }

    private void ShowStorageError(string message)
    {
        Shell.SetLoadFailed();
        StorageNotice.Severity = InfoBarSeverity.Error;
        StorageNotice.Title = UiText.Get("MainWindowStorage_SavedDataCouldNotBeLoaded");
        StorageNotice.Message = UiText.Get("MainWindowStorage_DataFolder_182", message, _services.DataDirectory.RootPath);
        StorageRetry.Visibility = Visibility.Visible;
    }

    private async void OnPreviewChoiceClick(object sender, RoutedEventArgs args)
    {
        if (!_previewSeeded || !Shell.CanEdit || sender is not MenuFlyoutItem choice || !Enum.TryParse(choice.Tag?.ToString(), out DesignScenarioKind kind)) { return; }
        if (await Shell.SelectPreviewAsync(kind)) { PreviewSelector.Content = choice.Text; }
    }
}

using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Controls;
using TunnelDeck.Core.Lifecycle;

namespace TunnelDeck;

public sealed partial class MainWindow
{
    private long _shutdownRevision;

    public void ActivateFromAnotherInstance() => DispatcherQueue.TryEnqueue(() =>
    {
        if (_closed) { return; }
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter) { presenter.Restore(); }
        Activate();
        SetForegroundWindow(_services.WindowHandle);
    });

    private void OnShutdownProgress(ShutdownProgress progress) => DispatcherQueue.TryEnqueue(() =>
    {
        if (_closed || progress.Revision < _shutdownRevision) { return; }
        _shutdownRevision = progress.Revision;
        ClosingNotice.Message = UiText.Translate(progress.Message);
        ClosingProgress.IsActive = progress.State == ShutdownState.Running;
        RuntimeStatus.Text = progress.State == ShutdownState.Running ? UiText.Get("MainWindowLifecycle_Closing") : UiText.Translate(progress.Message);
    });

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) { return; }
        args.Cancel = true;
        if (_closing) { return; }
        _closing = true;
        Navigation.IsEnabled = false;
        GlobalSearch.IsEnabled = false;
        CompactSearchButton.IsEnabled = false;
        StorageRetry.IsEnabled = false;
        ClosingNotice.IsOpen = true;
        ClosingNotice.Title = UiText.Get("MainWindow_ClosingTunnelDeck");
        ClosingNotice.Message = UiText.Get("MainWindowLifecycle_CancellingPendingWork");
        ClosingProgress.IsActive = true;
        UpdateNoticeVisibility();
        _services.BeginClosing();
        await Task.Yield();

        while (!_closed)
        {
            try
            {
                using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await _services.Dialogs.BeginClosingAsync(deadline.Token);
                await _services.Shutdown.ShutdownAsync(deadline.Token);
                await _services.Dialogs.CloseAsync();
                _allowClose = true;
                Close();
                return;
            }
            catch (Exception)
            {
                if (_closed) { return; }
                ClosingProgress.IsActive = false;
                ContentDialogResult result = await _services.Dialogs.ShowAsync(
                    () => new ShutdownDialog(_services.Shutdown.Current), shutdownDialog: true);
                if (result == ContentDialogResult.Primary) { ClosingProgress.IsActive = true; continue; }
                if (result == ContentDialogResult.Secondary)
                {
                    await _services.Dialogs.CloseAsync();
                    _allowClose = true;
                    Close();
                    // This explicit exit choice is the bounded fallback for cleanup or saving that cannot complete.
                    Environment.Exit(0);
                    return;
                }
                _closing = false;
                ClosingNotice.Title = UiText.Get("MainWindowLifecycle_ClosingPaused");
                ClosingNotice.Message = UiText.Get("MainWindowLifecycle_NewCommandsArePausedCloseTheWindowAgain");
                RuntimeStatus.Text = UiText.Get("MainWindowLifecycle_ClosingPaused");
                return;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}

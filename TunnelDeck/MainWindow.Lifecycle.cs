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
        ClosingNotice.Message = progress.Message;
        ClosingProgress.IsActive = progress.State == ShutdownState.Running;
        RuntimeStatus.Text = progress.State == ShutdownState.Running ? "Closing…" : progress.Message;
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
        ClosingNotice.Title = "Closing TunnelDeck";
        ClosingNotice.Message = "Cancelling pending work…";
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
                ClosingNotice.Title = "Closing paused";
                ClosingNotice.Message = "New commands are paused. Close the window again to retry cleanup or exit.";
                RuntimeStatus.Text = "Closing paused";
                return;
            }
        }
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
}

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Services;

/// <summary>One native ContentDialog at a time, including host approval and shutdown errors.</summary>
public sealed partial class DialogService : IHostKeyApproval, IDisposable
{
    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly CancellationTokenSource _closed = new();
    private readonly CancellationTokenSource _normalDialogsClosing = new();
    private FrameworkElement? _root;
    private DispatcherQueue? _dispatcher;
    private bool _disposed;

    public void Attach(FrameworkElement root) { _root = root; _dispatcher = root.DispatcherQueue; }

    public async Task<ContentDialogResult> ShowAsync(Func<ContentDialog> createDialog, CancellationToken cancellationToken = default, bool shutdownDialog = false)
    {
        using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _closed.Token,
            shutdownDialog ? CancellationToken.None : _normalDialogsClosing.Token);
        await _queue.WaitAsync(stopping.Token).ConfigureAwait(false);
        try
        {
            var completion = new TaskCompletionSource<ContentDialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (_dispatcher?.TryEnqueue(async () =>
            {
                try
                {
                    stopping.Token.ThrowIfCancellationRequested();
                    ContentDialog dialog = createDialog();
                    // Derived dialogs otherwise fall back to legacy control chrome. Apply the
                    // same Windows 11 template to every dialog, including dynamically created ones.
                    dialog.Style = (Style)Application.Current.Resources["AppContentDialogStyle"];
                    dialog.XamlRoot = _root!.XamlRoot;
                    dialog.RequestedTheme = _root.RequestedTheme;
                    dialog.Language = UiText.Language;
                    bool finished = false;
                    using CancellationTokenRegistration registration = stopping.Token.Register(() =>
                        _dispatcher.TryEnqueue(() =>
                        {
                            if (_disposed || finished) { completion.TrySetCanceled(stopping.Token); return; }
                            dialog.Hide();
                        }));
                    stopping.Token.ThrowIfCancellationRequested();
                    ContentDialogResult result;
                    try { result = await dialog.ShowAsync(); }
                    finally { finished = true; }
                    stopping.Token.ThrowIfCancellationRequested();
                    completion.TrySetResult(result);
                }
                catch (OperationCanceledException) { completion.TrySetCanceled(stopping.Token); }
                catch (Exception exception) { completion.TrySetException(exception); }
            }) != true)
            {
                throw new OperationCanceledException(UiText.Get("DialogService_TheWindowIsNoLongerAvailable"));
            }
            // Keep the queue slot until Hide has actually completed ShowAsync.
            return await completion.Task.ConfigureAwait(false);
        }
        finally { _queue.Release(); }
    }

    public async Task<bool> ConfirmAsync(HostKeyApprovalRequest request, CancellationToken cancellationToken) =>
        await ShowAsync(() => CreateHostDialog(request), cancellationToken).ConfigureAwait(false) == ContentDialogResult.Primary;

    private static ContentDialog CreateHostDialog(HostKeyApprovalRequest request)
    {
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(new TextBlock
        {
            Text = $"{request.ServerName}\n{request.Username}@{new NetworkEndpoint(request.Key.Host, request.Key.Port)}",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("DialogService_ThisIsTheFirstConnectionToThisHost"),
            TextWrapping = TextWrapping.Wrap,
        });
        var fingerprint = new TextBlock
        {
            Text = UiText.Get("DialogService_HostKey", request.Key.Algorithm, request.Key.Fingerprint),
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true,
        };
        AutomationProperties.SetAutomationId(fingerprint, "HostKeyFingerprint");
        content.Children.Add(fingerprint);
        content.Children.Add(new TextBlock
        {
            Text = UiText.Get("DialogService_TrustingSavesThisHostKeyOnThisComputer"),
            TextWrapping = TextWrapping.Wrap,
        });
        var dialog = new ContentDialog
        {
            Title = UiText.Get("DialogService_TrustThisSSHHost"), Content = content,
            PrimaryButtonText = UiText.Get("DialogService_TrustAndConnect"), CloseButtonText = UiText.Get("DeletionDialog_Cancel"), DefaultButton = ContentDialogButton.Close,
        };
        AutomationProperties.SetAutomationId(dialog, "HostKeyApproval");
        return dialog;
    }

    /// <summary>Dismiss and drain dialogs while their XamlRoot still belongs to a live window.</summary>
    public async Task BeginClosingAsync(CancellationToken cancellationToken)
    {
        await _normalDialogsClosing.CancelAsync().ConfigureAwait(false);
        await _queue.WaitAsync(cancellationToken).ConfigureAwait(false);
        _queue.Release();
    }

    public async Task CloseAsync()
    {
        await _closed.CancelAsync().ConfigureAwait(false);
        await _queue.WaitAsync().ConfigureAwait(false);
        _queue.Release();
    }

    public void Dispose() { _disposed = true; _closed.Cancel(); }
}

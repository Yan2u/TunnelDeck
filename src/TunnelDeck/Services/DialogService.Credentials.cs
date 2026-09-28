using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.Services;

public sealed partial class DialogService
{
    public async Task<ConnectionCredentials?> RequestForwardCredentialsAsync(RemoteServer server, string failure, CancellationToken cancellationToken)
    {
        string? secret = null;
        ContentDialogResult result = await ShowAsync(() =>
        {
            bool key = server.AuthMethod == AuthenticationMethod.PrivateKey;
            var password = new PasswordBox { Header = key ? UiText.Get("DialogServiceCredentials_PrivateKeyPassphrase") : UiText.Get("ServerDetailPage_Password") };
            AutomationProperties.SetAutomationId(password, "ForwardCredentialPassword");
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            AutomationProperties.SetAutomationId(error, "ForwardCredentialError");
            AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = ResourceDisplay.ServerName(server) + "\n" + ResourceDisplay.ServerEndpoint(server), TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = UiText.Translate(failure), TextWrapping = TextWrapping.Wrap });
            content.Children.Add(password);
            content.Children.Add(error);
            content.Children.Add(new TextBlock
            {
                Text = UiText.Get("DialogServiceCredentials_UsedForThisConnectionOnlyToRememberA") +
                    (key ? UiText.Get("DialogServiceCredentials_LeaveThePassphraseBlankForAnUnencryptedKey") : string.Empty),
                TextWrapping = TextWrapping.Wrap,
            });
            var dialog = new ContentDialog { Title = UiText.Get("DialogServiceCredentials_ConnectToForward"), Content = content, PrimaryButtonText = UiText.Get("DashboardPage_Forward"), CloseButtonText = UiText.Get("DeletionDialog_Cancel"), DefaultButton = ContentDialogButton.Primary };
            AutomationProperties.SetAutomationId(dialog, "ForwardCredentialDialog");
            dialog.Opened += (_, _) => password.Focus(FocusState.Programmatic);
            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (!key && string.IsNullOrEmpty(password.Password))
                { args.Cancel = true; error.Text = UiText.Get("DialogServiceCredentials_EnterTheSSHPassword"); error.Visibility = Visibility.Visible; password.Focus(FocusState.Programmatic); return; }
                secret = password.Password;
                password.Password = string.Empty;
            };
            dialog.Closed += (_, _) => password.Password = string.Empty;
            return dialog;
        }, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return result == ContentDialogResult.Primary ? new(secret) : null;
    }
}

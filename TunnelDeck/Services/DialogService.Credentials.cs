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
            var password = new PasswordBox { Header = key ? "Private-key passphrase" : "Password" };
            AutomationProperties.SetAutomationId(password, "ForwardCredentialPassword");
            var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Visibility = Visibility.Collapsed };
            AutomationProperties.SetAutomationId(error, "ForwardCredentialError");
            AutomationProperties.SetLiveSetting(error, AutomationLiveSetting.Polite);
            var content = new StackPanel { Spacing = 12 };
            content.Children.Add(new TextBlock { Text = ResourceDisplay.ServerName(server) + "\n" + ResourceDisplay.ServerEndpoint(server), TextWrapping = TextWrapping.Wrap });
            content.Children.Add(new TextBlock { Text = failure, TextWrapping = TextWrapping.Wrap });
            content.Children.Add(password);
            content.Children.Add(error);
            content.Children.Add(new TextBlock
            {
                Text = "Used for this connection only. To remember a credential, edit and save the server." +
                    (key ? " Leave the passphrase blank for an unencrypted key. Check the server's identity-file path if it could not be read." : string.Empty),
                TextWrapping = TextWrapping.Wrap,
            });
            var dialog = new ContentDialog { Title = "Connect to forward", Content = content, PrimaryButtonText = "Forward", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            AutomationProperties.SetAutomationId(dialog, "ForwardCredentialDialog");
            dialog.Opened += (_, _) => password.Focus(FocusState.Programmatic);
            dialog.PrimaryButtonClick += (_, args) =>
            {
                if (!key && string.IsNullOrEmpty(password.Password))
                { args.Cancel = true; error.Text = "Enter the SSH password."; error.Visibility = Visibility.Visible; password.Focus(FocusState.Programmatic); return; }
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

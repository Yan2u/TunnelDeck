using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Core.Lifecycle;

namespace TunnelDeck.Controls;

public sealed partial class ShutdownDialog : ContentDialog
{
    public string Heading { get; }
    public string Explanation { get; }
    public string ExitLabel { get; }
    public ShutdownDialog(ShutdownProgress progress)
    {
        Heading = progress.SavesData ? "Changes could not be saved" : "Cleanup did not finish";
        ExitLabel = progress.SavesData ? "Exit without saving" : "Exit now";
        Explanation = progress.SavesData
            ? "Your forwards have stopped, but some activity or configuration is still waiting to be saved. Retry saving, or exit without saving the remaining changes."
            : "Some forwards or connections have not finished closing. Retry cleanup, or exit now. Remote ports may remain in use until the SSH server detects the disconnected session.";
        InitializeComponent();
    }
}

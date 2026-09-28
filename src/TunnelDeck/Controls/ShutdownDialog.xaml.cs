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
        Heading = progress.SavesData ? UiText.Get("ShutdownDialogxaml_ChangesCouldNotBeSaved") : UiText.Get("ShutdownDialogxaml_CleanupDidNotFinish");
        ExitLabel = progress.SavesData ? UiText.Get("ShutdownDialogxaml_ExitWithoutSaving") : UiText.Get("ShutdownDialogxaml_ExitNow");
        Explanation = progress.SavesData
            ? UiText.Get("ShutdownDialogxaml_YourForwardsHaveStoppedButSomeActivityOr")
            : UiText.Get("ShutdownDialogxaml_SomeForwardsOrConnectionsHaveNotFinishedClosing");
        InitializeComponent();
    }
}

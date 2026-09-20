using Microsoft.UI.Xaml;

using TunnelDeck.Design;

namespace TunnelDeck;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        AppServices services = AppServices.Create(Program.Launch);
        _window ??= new MainWindow(services);
        Program.Instance.Attach(((MainWindow)_window).ActivateFromAnotherInstance);
        _window.Activate();
    }
}

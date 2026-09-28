using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TunnelDeck.Core.Navigation;
using TunnelDeck.ViewModels;

namespace TunnelDeck.Pages;

public class ShellPage : Page
{
    public ShellViewModel Shell { get; private set; } = null!;
    protected AppRoute Route { get; private set; } = AppRoute.Dashboard;

    protected override void OnNavigatedTo(NavigationEventArgs args)
    {
        base.OnNavigatedTo(args);
        var context = (PageContext)args.Parameter;
        Shell = context.Shell;
        Route = context.Route;
        Shell.StateUpdated += Refresh;
        Shell.PropertyChanged += OnShellPropertyChanged;
        InitializePage();
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        Shell.StateUpdated -= Refresh;
        Shell.PropertyChanged -= OnShellPropertyChanged;
        base.OnNavigatedFrom(args);
    }

    protected virtual void InitializePage() { }
    protected virtual void Refresh() { }
    protected virtual void OnAvailabilityChanged() { }
    protected void FocusInitialControl(Control control)
    {
        void QueueFocus() => DispatcherQueue.TryEnqueue(() =>
        {
            // Wait until navigation and its focus events have finished. A later navigation
            // or background state refresh must not focus a control on the previous page.
            if (IsLoaded && Shell.Navigation.Current == Route && control.IsEnabled)
            { control.Focus(FocusState.Programmatic); }
        });
        if (IsLoaded) { QueueFocus(); }
        else
        {
            void OnLoaded(object sender, RoutedEventArgs args) { Loaded -= OnLoaded; QueueFocus(); }
            Loaded += OnLoaded;
        }
    }
    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.CanEdit)) { OnAvailabilityChanged(); }
    }

    public static Visibility Visible(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public static Visibility Hidden(bool value) => value ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility TextVisible(string value) => string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible;
    public static bool HasText(string value) => !string.IsNullOrEmpty(value);
    protected static Thickness PagePadding(double width) => new(width < 680 ? 16 : 24);
}

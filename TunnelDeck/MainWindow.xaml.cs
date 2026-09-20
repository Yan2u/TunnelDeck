using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TunnelDeck.Core.Navigation;
using TunnelDeck.Design;
using TunnelDeck.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace TunnelDeck;

public sealed partial class MainWindow : Window
{
    private readonly AppServices _services;
    private bool _closing;
    private bool _allowClose;
    private bool _closed;
    private bool _syncingNavigation;
    public ShellViewModel Shell { get; }

    public MainWindow(AppServices services)
    {
        _services = services;
        Shell = new(services, DispatcherQueue);
        InitializeComponent();
        services.Dialogs.Attach(LayoutRoot);
        services.WindowHandle = WindowNative.GetWindowHandle(this);
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
        var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        int width = Math.Min((int)(1200 * scale), workArea.Width - (int)(32 * scale));
        int height = Math.Min((int)(760 * scale), workArea.Height - (int)(32 * scale));
        AppWindow.MoveAndResize(new RectInt32(workArea.X + (workArea.Width - width) / 2, workArea.Y + (workArea.Height - height) / 2, width, height));
        AppWindow.Closing += OnClosing;
        Closed += OnClosed;
        Shell.NavigationRequested += NavigateFrame;
        Shell.StateUpdated += RefreshSidebar;
        Shell.PropertyChanged += OnShellPropertyChanged;
        services.Shutdown.StateChanged += OnShutdownProgress;
        AllProfilesItem.Tag = AppRoute.Profiles;
        AllServersItem.Tag = AppRoute.Servers;
        SettingsItem.Tag = AppRoute.Settings;
        PreviewNotice.IsOpen = Shell.IsPreview;
        PreviewNotice.Visibility = Shell.IsPreview ? Visibility.Visible : Visibility.Collapsed;
        if (services.PreviewScenario is DesignScenarioKind preview) { PreviewSelector.Content = preview == DesignScenarioKind.Stress ? "Long lists" : preview.ToString(); }
        NavigateFrame(AppRoute.Dashboard);
    }

    private void OnPaneToggleRequested(TitleBar sender, object args) => Navigation.IsPaneOpen = !Navigation.IsPaneOpen;
    private void OnBackRequested(TitleBar sender, object args) => Shell.GoBack();
    private void OnBackAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args) { Shell.GoBack(); args.Handled = true; }
    private void OnDashboardClick(object sender, RoutedEventArgs args) => Shell.Navigate(AppRoute.Dashboard);
    private void OnSettingsClick(object sender, RoutedEventArgs args) => Shell.Navigate(AppRoute.Settings);
    private void OnEditErrorClosed(InfoBar sender, InfoBarClosedEventArgs args) => Shell.ClearError();

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShellViewModel.ThemeIndex))
        {
            LayoutRoot.RequestedTheme = Shell.ThemeIndex switch { 1 => ElementTheme.Light, 2 => ElementTheme.Dark, _ => ElementTheme.Default };
        }
        if (args.PropertyName is nameof(ShellViewModel.HasError) or nameof(ShellViewModel.IsDeleting)) { UpdateNoticeVisibility(); }
    }

    private void UpdateNoticeVisibility()
    {
        if (_closed) { return; }
        StorageNotice.Visibility = StorageNotice.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        EditErrorNotice.Visibility = Shell.HasError ? Visibility.Visible : Visibility.Collapsed;
        DeletionProgressNotice.Visibility = Shell.IsDeleting ? Visibility.Visible : Visibility.Collapsed;
        ClosingNotice.Visibility = ClosingNotice.IsOpen ? Visibility.Visible : Visibility.Collapsed;
        Notices.Visibility = StorageNotice.IsOpen || PreviewNotice.IsOpen || Shell.HasError || Shell.IsDeleting || ClosingNotice.IsOpen ? Visibility.Visible : Visibility.Collapsed;
    }
    private async void OnRetryDeletion(object sender, RoutedEventArgs args) => await Shell.RetryDeletionAsync();

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (GlobalSearch is null) { return; }
        double width = args.NewSize.Width;
        bool compact = width < 600;
        GlobalSearch.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        CompactSearchButton.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
        GlobalSearch.Width = width >= 1100 ? 288 : width >= 800 ? 240 : 184;
        GlobalSearch.PlaceholderText = width >= 1100 ? "Search profiles and servers" : "Search…";
        Notices.Margin = new Thickness(width < 680 ? 16 : 24, 8, width < 680 ? 16 : 24, 0);
    }


    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        AppWindow.Closing -= OnClosing;
        Closed -= OnClosed;
        Shell.NavigationRequested -= NavigateFrame;
        Shell.StateUpdated -= RefreshSidebar;
        Shell.PropertyChanged -= OnShellPropertyChanged;
        _services.Shutdown.StateChanged -= OnShutdownProgress;
        Shell.Dispose();
        _services.Dialogs.Dispose();
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
}

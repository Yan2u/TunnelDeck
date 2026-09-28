using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using TunnelDeck.Controls;
using TunnelDeck.Core.Navigation;
using TunnelDeck.Core.Presentation;
using TunnelDeck.ViewModels;

namespace TunnelDeck.Pages;

public sealed partial class DashboardPage : ShellPage
{
    private bool _refreshing;
    private bool _active;
    private bool _initialFocusPending = true;
    private readonly DispatcherTimer _recentTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    public DashboardViewModel ViewModel { get; } = new();
    public DashboardPage()
    {
        InitializeComponent();
        _recentTimer.Tick += (_, _) => ViewModel.RefreshTimes();
        Loaded += (_, _) => { FocusWhenReady(); _recentTimer.Start(); };
        Unloaded += (_, _) => _recentTimer.Stop();
    }

    protected override void InitializePage()
    {
        _active = true;
        Shell.TunnelsUpdated += RefreshTunnels;
        Bindings.Update();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs args)
    {
        _active = false;
        _recentTimer.Stop();
        Shell.TunnelsUpdated -= RefreshTunnels;
        base.OnNavigatedFrom(args);
    }

    protected override void Refresh()
    {
        _refreshing = true;
        try
        {
            ViewModel.Update(Shell.State);
            QuickServer.SelectedItem = Shell.ServerChoices.FirstOrDefault(server => server.Id == Shell.Quick.SelectedServerId);
            NoServerHint.Visibility = Visible(Shell.State.Servers.IsEmpty);
            RefreshTunnels();
            EmptyRecent.Visibility = Visible(ViewModel.Recent.Count == 0);
            UpdateQuickPin();
        }
        finally { _refreshing = false; }
        FocusWhenReady();
    }

    private void FocusWhenReady()
    {
        if (!_active || !IsLoaded || !Shell.IsReady || !_initialFocusPending) { return; }
        _initialFocusPending = false;
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!_active) { return; }
            if (Shell.Quick.SelectedServerId is not null) { (Shell.Quick.UseAdvanced ? LocalPort : QuickPort).FocusInput(); }
            else { QuickServer.Focus(FocusState.Keyboard); }
        });
    }

    protected override void OnAvailabilityChanged() => UpdateQuickPin();

    private void RefreshTunnels()
    {
        EmptyTunnels.Visibility = Visible(Shell.Tunnels.Count == 0);
        ActiveList.Visibility = Hidden(Shell.Tunnels.Count == 0);
    }

    private async void OnPortSubmitted(object? sender, EventArgs args) => await SubmitAsync();
    private async void OnForwardClick(object sender, RoutedEventArgs args) => await SubmitAsync();
    private async Task SubmitAsync()
    {
        if (!_active || Shell.Quick.IsBusy) { return; }
        // Capture before NumberBox's key-up validation can normalize text or retain an older Value.
        if (Shell.Quick.UseAdvanced)
        {
            Shell.Quick.LocalPortText = LocalPort.RawText;
            Shell.Quick.RemotePortText = RemotePort.RawText;
        }
        else { Shell.Quick.PortText = QuickPort.RawText; }
        string submitted = Shell.Quick.UseAdvanced ? LocalPort.RawText : QuickPort.RawText;
        await Shell.Forwarding.SubmitQuickAsync();
        if (!_active) { return; }
        DependencyObject? focus = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
        while (focus is not null && focus != QuickForm) { focus = VisualTreeHelper.GetParent(focus); }
        if (focus == QuickForm && !Shell.Quick.HasErrors)
        {
            PortInput target = Shell.Quick.UseAdvanced ? LocalPort : QuickPort;
            if (target.RawText == submitted) { target.FocusInput(selectAll: true); }
        }
    }

    private async void OnStopTunnel(object? sender, TunnelItem item) => await Shell.Forwarding.StopAsync(item);
    private async void OnRetryTunnel(object? sender, TunnelItem item) => await Shell.Forwarding.RetryAsync(item);
    private async void OnRemoveTunnel(object? sender, TunnelItem item) => await Shell.Forwarding.RemoveAsync(item);
    private async void OnStopAll(object sender, RoutedEventArgs args) => await Shell.Forwarding.StopAllAsync();
    private async void OnRetryActivity(object sender, RoutedEventArgs args) => await Shell.Forwarding.RetryActivityAsync();
    private void OnToggleRecent(object sender, RoutedEventArgs args) => ViewModel.ToggleRecent();
    private void OnCommandErrorClosed(InfoBar sender, InfoBarClosedEventArgs args) => Shell.Forwarding.ClearCommandError();
    private void OnDetailsRefresh(object? sender, TunnelItem item) => Shell.Forwarding.RefreshDetails(item);
    private void OnOpenTunnelServer(object? sender, TunnelItem item) { if (item.CanOpenServer) { Shell.Navigate(item.ServerRoute); } }
    private void OnOpenTunnelProfile(object? sender, TunnelItem item) { if (item.CanOpenProfile) { Shell.Navigate(item.ProfileRoute); } }

    private void OnServerSelected(object sender, SelectionChangedEventArgs args)
    {
        // ItemsSource initialization/replacement briefly clears selection. Only a real choice
        // changes the draft; configuration reconciliation owns removal of a deleted server.
        if (_refreshing || Shell is null || QuickServer.SelectedItem is not ResourceRow selected) { return; }
        Shell.Quick.SelectedServerId = selected.Id;
        UpdateQuickPin();
    }

    private void UpdateQuickPin()
    {
        bool pinned = Shell.Quick.SelectedServerId is Guid id && Shell.State.Settings.PinnedQuickServerId == id;
        QuickPin.IsEnabled = Shell.CanEdit && Shell.Quick.SelectedServerId is not null;
        QuickOpenTerminal.IsEnabled = Shell.CanOpenTerminal && Shell.Quick.SelectedServerId is not null;
        QuickPinIcon.Glyph = pinned ? "\uE77A" : "\uE718";
        ToolTipService.SetToolTip(QuickPin, pinned ? UiText.Get("DashboardPagexaml_UnpinQuickForwardServer") : UiText.Get("DashboardPagexaml_UseThisServerForQuickForward"));
    }

    private async void OnQuickPinClick(object sender, RoutedEventArgs args)
    {
        Guid? selected = Shell.Quick.SelectedServerId;
        await Shell.SaveSettingsAsync(settings => settings with { PinnedQuickServerId = settings.PinnedQuickServerId == selected ? null : selected });
        UpdateQuickPin();
    }

    private void OnAddServerClick(object sender, RoutedEventArgs args) => Shell.Navigate(AppRoute.Server());
    private async void OnTerminalClick(object sender, RoutedEventArgs args)
    { if (Shell.Quick.SelectedServerId is Guid id) { await Shell.OpenTerminalAsync(id); } }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (PageContent is null) { return; }
        PageContent.Padding = PagePadding(args.NewSize.Width);
        bool wide = args.NewSize.Width >= 760;
        RecentAndStatistics.ColumnDefinitions[1].Width = wide ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        Grid.SetColumn(StatisticsSection, wide ? 1 : 0);
        Grid.SetRow(StatisticsSection, wide ? 0 : 1);
        ActiveList.MaxHeight = Math.Clamp(args.NewSize.Height * 0.45, 180, 420);
    }

    private void OnQuickFormSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (ServerField is null) { return; }
        bool inline = args.NewSize.Width >= 600;
        QuickFields.RowSpacing = inline ? 0 : 12;
        Grid.SetColumnSpan(ServerField, inline ? 1 : 2);
        Grid.SetRow(ForwardFields, inline ? 0 : 1);
        Grid.SetColumn(ForwardFields, inline ? 1 : 0);
        Grid.SetColumnSpan(ForwardFields, inline ? 1 : 2);
        ForwardModeSelector.Width = inline ? 144 : double.NaN;
        Grid.SetColumnSpan(AutoFindPort, inline ? 1 : 3);
        Grid.SetRow(AdvancedToggle, inline ? 0 : 1);
        Grid.SetColumn(AdvancedToggle, inline ? 1 : 0);
        AdvancedToggle.HorizontalAlignment = inline ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        Grid.SetRow(QuickOpenTerminal, inline ? 0 : 1);
    }
}

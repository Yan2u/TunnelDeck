using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using TunnelDeck.ViewModels;

namespace TunnelDeck.Pages;

public sealed partial class SettingsPage : ShellPage
{
    private bool _refreshing;
    public SettingsViewModel ViewModel { get; } = new();
    public SettingsPage() => InitializeComponent();
    protected override void InitializePage() { ViewModel.Load(Shell); Bindings.Update(); }
    protected override void OnAvailabilityChanged() => ViewModel.RefreshAvailability();
    protected override void OnNavigatedFrom(NavigationEventArgs args) { ViewModel.Unload(); base.OnNavigatedFrom(args); }

    protected override void Refresh()
    {
        _refreshing = true;
        try
        {
            LanguageSelector.SelectedIndex = ViewModel.LanguageIndex;
            SidebarMaximum.Value = Shell.State.Settings.SidebarMaximumItems;
            AutoFindDefault.IsOn = Shell.State.Settings.AutoFindAvailablePortDefault;
            ServerOption[] choices = [new(null, UiText.Get("SettingsPagexaml_None")), .. Shell.ServerChoices.Select(server => new ServerOption(server.Id, server.ChoiceLabel))];
            DefaultServer.ItemsSource = choices;
            DefaultServer.SelectedItem = choices.FirstOrDefault(choice => choice.Id == Shell.State.Settings.DefaultServerId) ?? choices[0];
            ViewModel.ReconcileAuthentication();
        }
        finally { _refreshing = false; }
    }

    private async void OnMaximumChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_refreshing || Shell is null || !ViewModel.CanEdit) { return; }
        await ViewModel.SetMaximumAsync(args.NewValue);
        Refresh();
    }

    private async void OnAutoFindToggled(object sender, RoutedEventArgs args)
    {
        if (_refreshing || Shell is null || !ViewModel.CanEdit) { return; }
        bool value = AutoFindDefault.IsOn;
        await ViewModel.SetAutoFindAsync(value);
        Refresh();
    }

    private async void OnDefaultServerChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_refreshing || Shell is null || !ViewModel.CanEdit || DefaultServer.SelectedItem is not ServerOption selected) { return; }
        await ViewModel.SetDefaultServerAsync(selected.Id);
        Refresh();
    }

    private async void OnRetryClick(object sender, RoutedEventArgs args) { await ViewModel.RetryAsync(); Refresh(); }

    private async void OnLanguageChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_refreshing || Shell is null || !ViewModel.CanEdit || LanguageSelector.SelectedIndex < 0) { return; }
        await ViewModel.SetLanguageAsync(LanguageSelector.SelectedIndex);
        Refresh();
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (PageContent is null) { return; }
        PageContent.Padding = PagePadding(args.NewSize.Width);
        double contentWidth = Math.Min(PageContent.MaxWidth, args.NewSize.Width) - PageContent.Padding.Left - PageContent.Padding.Right;
        bool compact = contentWidth < 680;
        foreach (DevWinUI.SettingsCard card in new[] { LanguageCard, ThemeCard, SidebarCard, DefaultServerCard, AutoFindCard, AuthenticationCard })
        {
            card.ContentAlignment = compact ? DevWinUI.ContentAlignment.Vertical : DevWinUI.ContentAlignment.Right;
            card.HorizontalContentAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
            // The library's size triggers can retain the previous presenter state after a pane resize.
            card.ApplyTemplate();
            VisualStateManager.GoToState(card, compact ? "Vertical" : "Right", false);
        }
        foreach (ComboBox selector in new[] { LanguageSelector, ThemeSelector, DefaultServer, DefaultAuthentication })
        {
            selector.Width = compact ? double.NaN : 240;
            selector.HorizontalAlignment = compact ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
        }
        SidebarMaximum.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
        AutoFindDefault.HorizontalAlignment = compact ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }
    public sealed record ServerOption(Guid? Id, string Name);
}

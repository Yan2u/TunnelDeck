using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TunnelDeck.Pages;

public sealed partial class SettingsPage : ShellPage
{
    private bool _refreshing;
    public SettingsPage() => InitializeComponent();
    protected override void InitializePage() => Bindings.Update();

    protected override void Refresh()
    {
        _refreshing = true;
        try
        {
            SidebarMaximum.Value = Shell.State.Settings.SidebarMaximumItems;
            AutoFindDefault.IsOn = Shell.State.Settings.AutoFindAvailablePortDefault;
            ServerOption[] choices = [new(null, "None"), .. Shell.ServerChoices.Select(server => new ServerOption(server.Id, server.ChoiceLabel))];
            DefaultServer.ItemsSource = choices;
            DefaultServer.SelectedItem = choices.FirstOrDefault(choice => choice.Id == Shell.State.Settings.DefaultServerId) ?? choices[0];
        }
        finally { _refreshing = false; }
    }

    private async void OnMaximumChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_refreshing || Shell is null || !Shell.CanEdit) { return; }
        if (!double.IsFinite(args.NewValue) || args.NewValue is < 3 or > 10 || args.NewValue % 1 != 0)
        {
            Shell.ReportError("Choose a whole number from 3 to 10 for sidebar items.");
            return;
        }
        if (!await Shell.SaveSettingsAsync(settings => settings with { SidebarMaximumItems = (int)args.NewValue })) { Refresh(); }
    }

    private async void OnAutoFindToggled(object sender, RoutedEventArgs args)
    {
        if (_refreshing || Shell is null || !Shell.CanEdit) { return; }
        bool value = AutoFindDefault.IsOn;
        if (!await Shell.SaveSettingsAsync(settings => settings with { AutoFindAvailablePortDefault = value })) { Refresh(); }
    }

    private async void OnDefaultServerChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_refreshing || Shell is null || !Shell.CanEdit || DefaultServer.SelectedItem is not ServerOption selected) { return; }
        if (!await Shell.SaveSettingsAsync(settings => settings with { DefaultServerId = selected.Id })) { Refresh(); }
    }

    private void OnPageSizeChanged(object sender, SizeChangedEventArgs args) { if (PageContent is not null) { PageContent.Padding = PagePadding(args.NewSize.Width); } }
    public sealed record ServerOption(Guid? Id, string Name);
}

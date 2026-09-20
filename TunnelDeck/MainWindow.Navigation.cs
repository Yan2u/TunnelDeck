using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Navigation;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Pages;
using TunnelDeck.ViewModels;

namespace TunnelDeck;

public sealed partial class MainWindow
{
    private void NavigateFrame(AppRoute route)
    {
        Type page = route.Destination switch
        {
            NavigationDestination.Dashboard => typeof(DashboardPage),
            NavigationDestination.Profiles => typeof(ProfilesPage),
            NavigationDestination.ProfileDetail => typeof(ProfileDetailPage),
            NavigationDestination.Servers => typeof(ServersPage),
            NavigationDestination.ServerDetail => typeof(ServerDetailPage),
            NavigationDestination.Settings => typeof(SettingsPage),
            _ => throw new ArgumentOutOfRangeException(nameof(route)),
        };
        ContentFrame.Navigate(page, new PageContext(Shell, route), new EntranceNavigationTransitionInfo());
        UpdateNavigationSelection();
        if (_storageStarted && Navigation.DisplayMode == NavigationViewDisplayMode.Minimal) { Navigation.IsPaneOpen = false; }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_syncingNavigation || args.SelectedItem is not NavigationViewItem { Tag: AppRoute route }) { return; }
        Shell.Navigate(route);
    }

    private void OnNavigationItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        // A detail outside the working set highlights its All entry. Invoking that already-selected
        // entry must still navigate back to the list; SelectionChanged alone cannot report it.
        if (!_syncingNavigation && args.InvokedItemContainer is NavigationViewItem { Tag: AppRoute route }) { Shell.Navigate(route); }
    }

    private void RefreshSidebar()
    {
        if (_closed) { return; }
        _syncingNavigation = true;
        try
        {
            SidebarItems projection = SidebarProjection.Project(Shell.State);
            var profileRows = Shell.Resources(ResourceKind.Profile, true).Concat(Shell.Resources(ResourceKind.Profile, false)).ToDictionary(row => row.Id);
            var serverRows = Shell.Resources(ResourceKind.Server, true).Concat(Shell.Resources(ResourceKind.Server, false)).ToDictionary(row => row.Id);
            Navigation.MenuItems.Clear();
            Navigation.MenuItems.Add(new NavigationViewItemHeader { Content = "Profiles" });
            foreach (Guid id in projection.ProfileIds)
            {
                AddSidebarItem(profileRows[id]);
            }
            Navigation.MenuItems.Add(AllProfilesItem);
            Navigation.MenuItems.Add(new NavigationViewItemHeader { Content = "Servers" });
            foreach (Guid id in projection.ServerIds)
            {
                AddSidebarItem(serverRows[id]);
            }
            Navigation.MenuItems.Add(AllServersItem);
        }
        finally { _syncingNavigation = false; }
        UpdateNavigationSelection();
        if (!string.IsNullOrWhiteSpace(GlobalSearch.Text)) { UpdateSearch(GlobalSearch); }
        if (!string.IsNullOrWhiteSpace(CompactSearch.Text)) { UpdateSearch(CompactSearch); }
    }

    private void AddSidebarItem(ResourceRow row)
    {
        var item = new NavigationViewItem
        {
            Content = row.Name, Tag = row.Route,
            Icon = new FontIcon { Glyph = row.IsPinned ? "\uE718" : row.Glyph },
        };
        AutomationProperties.SetAutomationId(item, $"Sidebar{row.Kind}_{row.Id:N}");
        AutomationProperties.SetName(item, row.AccessibleName);
        ToolTipService.SetToolTip(item, row.AccessibleName);
        Navigation.MenuItems.Add(item);
    }

    private void UpdateNavigationSelection()
    {
        _syncingNavigation = true;
        try
        {
            AppRoute route = Shell.Navigation.Current;
            Navigation.SelectedItem = Navigation.MenuItems.Concat(Navigation.FooterMenuItems)
                .OfType<NavigationViewItem>().FirstOrDefault(item => item.Tag is AppRoute target && target == route);
            if (Navigation.SelectedItem is null && route.ResourceId is not null)
            {
                Navigation.SelectedItem = route.Destination == NavigationDestination.ProfileDetail ? AllProfilesItem : AllServersItem;
            }
            AutomationProperties.SetHelpText(DashboardButton, route == AppRoute.Dashboard ? "Current page" : "Open dashboard");
            AutomationProperties.SetHelpText(SettingsItem, route == AppRoute.Settings ? "Current page" : "Open settings");
        }
        finally { _syncingNavigation = false; }
    }

    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) { return; }
        UpdateSearch(sender);
    }

    private void UpdateSearch(AutoSuggestBox sender)
    {
        var results = ResourceSearch.Find(Shell.State, sender.Text);
        sender.ItemsSource = results.IsEmpty && !string.IsNullOrWhiteSpace(sender.Text)
            ? new[] { new SearchSuggestion(null) } : results.Select(result => new SearchSuggestion(result)).ToArray();
    }

    private void OnSuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is SearchSuggestion { Result: not null } suggestion) { sender.Text = suggestion.Name; }
    }

    private void OnSearchSubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        ResourceSearchResult? result = (args.ChosenSuggestion as SearchSuggestion)?.Result;
        if (args.ChosenSuggestion is null)
        {
            var matches = ResourceSearch.Find(Shell.State, sender.Text);
            if (matches.Length == 1) { result = matches[0]; }
        }
        if (result is not null)
        {
            Shell.Navigate(result.Route);
            sender.IsSuggestionListOpen = false;
            CompactSearchFlyout.Hide();
            sender.Text = string.Empty;
        }
        else { UpdateSearch(sender); sender.IsSuggestionListOpen = !string.IsNullOrWhiteSpace(sender.Text); }
    }

    private void OnSearchAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (CompactSearchButton.Visibility == Visibility.Visible) { CompactSearchFlyout.ShowAt(CompactSearchButton); }
        else { GlobalSearch.Focus(FocusState.Keyboard); }
        args.Handled = true;
    }

    private void OnCompactSearchOpened(object? sender, object args) => CompactSearch.Focus(FocusState.Keyboard);
}

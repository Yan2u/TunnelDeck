using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using TunnelDeck.Controls;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Navigation;
using TunnelDeck.ViewModels;

namespace TunnelDeck;

public sealed partial class MainWindow
{
    private void OnSidebarContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not NavigationViewItem { Tag: AppRoute { ResourceId: Guid id } route } item) { return; }
        ResourceKind kind = route.Destination == NavigationDestination.ProfileDetail ? ResourceKind.Profile : ResourceKind.Server;
        ResourceRow? row = Shell.Resources(kind, true).Concat(Shell.Resources(kind, false)).FirstOrDefault(value => value.Id == id);
        if (row is null) { return; }
        MenuFlyout menu = ResourceActions.Create(row, OnResourceOpen, OnResourcePin, OnResourceMoveUp, OnResourceMoveDown, OnResourceRemove);
        if (args.TryGetPosition(item, out var position)) { menu.ShowAt(item, new FlyoutShowOptions { Position = position }); }
        else { menu.ShowAt(item); }
        args.Handled = true;
    }

    private void OnResourceOpen(object sender, RoutedEventArgs args) { if (sender is FrameworkElement { Tag: ResourceRow row }) { Shell.Navigate(row.Route); } }
    private async void OnResourcePin(object sender, RoutedEventArgs args) { if (sender is FrameworkElement { Tag: ResourceRow row }) { await Shell.TogglePinAsync(row); } }
    private async void OnResourceMoveUp(object sender, RoutedEventArgs args) { if (sender is FrameworkElement { Tag: ResourceRow row }) { await Shell.MovePinnedAsync(row, -1); } }
    private async void OnResourceMoveDown(object sender, RoutedEventArgs args) { if (sender is FrameworkElement { Tag: ResourceRow row }) { await Shell.MovePinnedAsync(row, 1); } }
    private async void OnResourceRemove(object sender, RoutedEventArgs args) { if (sender is FrameworkElement { Tag: ResourceRow row }) { await Shell.DeleteResourceAsync(row.Kind, row.Id); } }
}

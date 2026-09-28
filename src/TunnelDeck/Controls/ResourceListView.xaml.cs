using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Navigation;
using TunnelDeck.ViewModels;

namespace TunnelDeck.Controls;

public sealed partial class ResourceListView : UserControl
{
    private ShellViewModel? _shell;
    private ResourceKind _kind;
    private bool _saving;
    private Func<Task<bool>>? _retry;
    public ResourceListViewModel ViewModel { get; } = new();
    public ResourceListView() => InitializeComponent();
    public void SetAvailability(bool canEdit) => ViewModel.SetAvailability(canEdit);

    public void Update(ShellViewModel shell, ResourceKind kind)
    {
        _shell = shell;
        _kind = kind;
        if (!_dragging) { ViewModel.Update(shell, kind); }
        UpdateListViewports();
        ViewModel.TransferNotice = shell.TransferNotice;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(AddResourceButton, kind == ResourceKind.Profile ? "AddProfile" : "AddServer");
    }

    private void OnAddClick(object sender, RoutedEventArgs args) => _shell?.Navigate(_kind == ResourceKind.Profile ? AppRoute.Profile() : AppRoute.Server());
    private void OnImportClick(object sender, RoutedEventArgs args) { if (_shell?.CanEdit == true) { _shell.Navigate(AppRoute.Import); } }
    private void OnExportClick(object sender, RoutedEventArgs args)
    { if (_shell?.CanEdit == true) { _shell.Navigate(_kind == ResourceKind.Profile ? AppRoute.ExportProfiles : AppRoute.ExportServers); } }
    private void OnTransferNoticeClosed(InfoBar sender, InfoBarClosedEventArgs args) { _shell?.ClearTransferNotice(); ViewModel.TransferNotice = string.Empty; }
    private void OnItemClick(object sender, ItemClickEventArgs args) { if (args.ClickedItem is ResourceRow row) { _shell?.Navigate(row.Route); } }
    private void OnOpenClick(object sender, RoutedEventArgs args) { if ((sender as FrameworkElement)?.Tag is ResourceRow row) { _shell?.Navigate(row.Route); } }
    private MenuFlyout CreateMenu(ResourceRow row) => ResourceActions.Create(row, OnOpenClick, OnPinClick, OnMoveUpClick, OnMoveDownClick, OnDeleteClick);
    private void OnMoreClick(object sender, RoutedEventArgs args)
    {
        if (sender is Button { Tag: ResourceRow row } button) { CreateMenu(row).ShowAt(button); }
    }
    private void OnResourceContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if (sender is not FrameworkElement { Tag: ResourceRow row } element) { return; }
        MenuFlyout menu = CreateMenu(row);
        if (args.TryGetPosition(element, out var position)) { menu.ShowAt(element, new FlyoutShowOptions { Position = position }); }
        else { menu.ShowAt(element); }
        args.Handled = true;
    }
    private async void OnPinClick(object sender, RoutedEventArgs args)
    {
        if (_shell is not null && (sender as FrameworkElement)?.Tag is ResourceRow row) { await SaveChangeAsync(() => _shell.TogglePinAsync(row)); }
    }
    private async void OnMoveUpClick(object sender, RoutedEventArgs args)
    {
        if (_shell is not null && (sender as FrameworkElement)?.Tag is ResourceRow row) { await SaveChangeAsync(() => _shell.MovePinnedAsync(row, -1)); }
    }
    private async void OnMoveDownClick(object sender, RoutedEventArgs args)
    {
        if (_shell is not null && (sender as FrameworkElement)?.Tag is ResourceRow row) { await SaveChangeAsync(() => _shell.MovePinnedAsync(row, 1)); }
    }
    private async void OnDeleteClick(object sender, RoutedEventArgs args)
    {
        if (_shell is null || (sender as FrameworkElement)?.Tag is not ResourceRow row) { return; }
        _retry = null; ViewModel.OperationError = string.Empty;
        await _shell.DeleteResourceAsync(row.Kind, row.Id);
    }
    private async void OnDeleteAllClick(object sender, RoutedEventArgs args)
    {
        if (_shell is null) { return; }
        _retry = null; ViewModel.OperationError = string.Empty;
        await _shell.DeleteResourceAsync(_kind);
    }
    private async void OnRetryChange(object sender, RoutedEventArgs args) { if (_retry is not null) { await SaveChangeAsync(_retry); } }
    private async Task SaveChangeAsync(Func<Task<bool>> change)
    {
        if (_shell is null || _saving || !ViewModel.CanEdit) { return; }
        _saving = true; ViewModel.OperationError = string.Empty;
        bool saved = false;
        try { saved = await change(); }
        finally { _saving = false; ViewModel.Update(_shell, _kind); }
        _retry = saved ? null : change;
        if (!saved)
        {
            ViewModel.OperationError = string.IsNullOrEmpty(_shell.Error) ? UiText.Get("ResourceListViewxaml_TheChangeCouldNotBeSavedRetryOr") : _shell.Error;
            _shell.ClearError();
        }
    }
    private void OnControlSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (PageContent is not null) { PageContent.Padding = new Thickness(args.NewSize.Width < 680 ? 16 : 24); }
    }

    private void OnViewportSizeChanged(object sender, SizeChangedEventArgs args) => UpdateListViewports();
    private void OnCollectionLoaded(object sender, RoutedEventArgs args) => UpdateCardWidth((GridView)sender);
    private void OnCollectionSizeChanged(object sender, SizeChangedEventArgs args) => UpdateCardWidth((GridView)sender);
    private static void UpdateCardWidth(GridView view)
    {
        if (view.ItemsPanelRoot is not ItemsWrapGrid panel || view.ActualWidth <= 0) { return; }
        double available = Math.Max(1, view.ActualWidth - 16);
        int columns = Math.Clamp((int)(available / 304), 1, 3);
        panel.MaximumRowsOrColumns = columns;
        panel.ItemWidth = available / columns;
    }

    private void UpdateListViewports()
    {
        if (ResourceViewport.ActualHeight <= 0) { return; }
        // Each native ListView must receive a finite height for virtualization. Keep Other reachable
        // even when there are many pinned resources; an all-pinned collection can use the full viewport.
        PinnedSection.MaxHeight = ResourceViewport.ActualHeight * (ViewModel.Other.Count == 0 ? 1 : 0.4);
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(AddResourceButton, kind == ResourceKind.Profile ? "AddProfile" : "AddServer");
    }

    private void OnAddClick(object sender, RoutedEventArgs args) => _shell?.Navigate(_kind == ResourceKind.Profile ? AppRoute.Profile() : AppRoute.Server());
    private void OnItemClick(object sender, ItemClickEventArgs args) { if (args.ClickedItem is ResourceRow row) { _shell?.Navigate(row.Route); } }
    private void OnOpenClick(object sender, RoutedEventArgs args) { if ((sender as FrameworkElement)?.Tag is ResourceRow row) { _shell?.Navigate(row.Route); } }
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
            ViewModel.OperationError = string.IsNullOrEmpty(_shell.Error) ? "The change could not be saved. Retry or make a new change." : _shell.Error;
            _shell.ClearError();
        }
    }
    private void OnControlSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (PageContent is not null) { PageContent.Padding = new Thickness(args.NewSize.Width < 680 ? 16 : 24); }
    }
}

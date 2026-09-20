using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace TunnelDeck.Controls;

public sealed partial class ResourceListView
{
    private const string DragOwnerKey = "TunnelDeck.PinnedList";
    private readonly string _dragOwner = Guid.NewGuid().ToString("N");
    private bool _dragging;
    private Guid[] _dragOrder = [];

    private void OnPinnedDragStarting(object sender, DragItemsStartingEventArgs args)
    {
        if (!ViewModel.CanEdit || _saving || args.Items.Count == 0 || args.Items.OfType<ResourceRow>().Any(row => !row.IsPinned || row.Kind != _kind))
        { args.Cancel = true; return; }
        _dragging = true;
        _dragOrder = ViewModel.Pinned.Select(row => row.Id).ToArray();
        args.Data.Properties[DragOwnerKey] = _dragOwner;
        args.Data.RequestedOperation = DataPackageOperation.Move;
    }
    private bool OwnsDrag(DragEventArgs args) => _dragging && ViewModel.CanEdit &&
        args.DataView.Properties.TryGetValue(DragOwnerKey, out object? owner) && Equals(owner, _dragOwner);
    private void OnPinnedDragOver(object sender, DragEventArgs args)
    {
        if (OwnsDrag(args)) { args.AcceptedOperation = DataPackageOperation.Move; }
        else { RejectDrop(sender, args); }
    }
    private void OnPinnedDrop(object sender, DragEventArgs args) { if (!OwnsDrag(args)) { RejectDrop(sender, args); } }
    private void RejectDrop(object sender, DragEventArgs args) { args.AcceptedOperation = DataPackageOperation.None; args.Handled = true; }

    private async void OnPinnedDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (!_dragging || _shell is null) { return; }
        Guid[] expected = _dragOrder, proposed = ViewModel.Pinned.Select(row => row.Id).ToArray();
        _dragging = false; _dragOrder = [];
        if ((args.DropResult & DataPackageOperation.Move) != 0 && !expected.SequenceEqual(proposed))
        { await SaveChangeAsync(() => _shell.ReorderPinnedAsync(_kind, expected, proposed)); }
        // Includes rejected cross-list drops, cancellation, conflicts and failed persistence.
        ViewModel.Update(_shell, _kind);
    }
}

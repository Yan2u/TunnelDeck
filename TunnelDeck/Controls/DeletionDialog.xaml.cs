using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.Controls;

public sealed partial class DeletionDialog : ContentDialog
{
    private XamlRoot? _dialogRoot;
    public DeletionConfirmationModel Model { get; }
    public DeletionDialog(DeletionConfirmationModel model) { Model = model; InitializeComponent(); }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _dialogRoot = XamlRoot;
        _dialogRoot.Changed += OnRootChanged;
        ResizeBody();
        if (GetTemplateChild("CloseButton") is Button cancel) { cancel.Focus(FocusState.Keyboard); }
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ResizeBody();
    private void ResizeBody() => BodyScroll.MaxHeight = Math.Max(100, XamlRoot.Size.Height - 260);
    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        if (_dialogRoot is null) { return; }
        _dialogRoot.Changed -= OnRootChanged;
        _dialogRoot = null;
    }
}

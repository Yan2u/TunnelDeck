using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Transfer;

namespace TunnelDeck.Controls;

public sealed partial class TransferPreviewItem : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item), typeof(ImportItem), typeof(TransferPreviewItem), new PropertyMetadata(null));
    public ImportItem Item { get => (ImportItem)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public TransferPreviewItem() => InitializeComponent();
    public string ResourceGlyph(ImportItem? item) => item?.Kind == ResourceKind.Profile ? "\uE8F1" : "\uE968";
}

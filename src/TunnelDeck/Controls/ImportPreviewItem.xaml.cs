using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.ViewModels;

namespace TunnelDeck.Controls;

public sealed partial class ImportPreviewItem : UserControl
{
    public static readonly DependencyProperty EntryProperty = DependencyProperty.Register(
        nameof(Entry), typeof(ImportPreviewEntry), typeof(ImportPreviewItem), new PropertyMetadata(null));

    public ImportPreviewEntry Entry { get => (ImportPreviewEntry)GetValue(EntryProperty); set => SetValue(EntryProperty, value); }

    public ImportPreviewItem() => InitializeComponent();
}

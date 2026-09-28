using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.Controls;

public sealed partial class RecentActivityListItem : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item), typeof(RecentActivityItem), typeof(RecentActivityListItem), new PropertyMetadata(null));
    public RecentActivityItem Item { get => (RecentActivityItem)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public RecentActivityListItem() => InitializeComponent();
}

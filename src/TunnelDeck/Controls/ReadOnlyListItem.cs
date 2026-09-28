using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace TunnelDeck.Controls;

// ItemsRepeater does not provide item containers. Expose a named, readable list item
// for each row without adding a tab stop to content that has no action.
public sealed class ReadOnlyListItem : ContentControl
{
    public ReadOnlyListItem()
    {
        IsTabStop = false;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new ReadOnlyListItemAutomationPeer(this);

    private sealed class ReadOnlyListItemAutomationPeer(ReadOnlyListItem owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(ReadOnlyListItem);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;
    }
}

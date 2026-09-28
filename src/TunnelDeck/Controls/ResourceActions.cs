using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.ViewModels;

namespace TunnelDeck.Controls;

/// <summary>The same commands serve a card's context menu, overflow and sidebar entry.</summary>
internal static class ResourceActions
{
    public static MenuFlyout Create(ResourceRow row, RoutedEventHandler open, RoutedEventHandler pin,
        RoutedEventHandler moveUp, RoutedEventHandler moveDown, RoutedEventHandler remove)
    {
        var menu = new MenuFlyout();
        Add(UiText.Get("Common_Open"), new SymbolIcon(Symbol.OpenFile), open, automationId: "Open" + row.RowAutomationId);
        Add(row.PinLabel, new SymbolIcon(Symbol.Pin), pin, row.CanEdit, "Menu" + row.PinAutomationId);
        if (row.IsPinned)
        {
            menu.Items.Add(new MenuFlyoutSeparator());
            Add(UiText.Get("ProfileDetailPage_MoveUp"), new FontIcon { Glyph = "\uE70E" }, moveUp, row.CanMoveUp, row.MoveUpAutomationId);
            Add(UiText.Get("ProfileDetailPage_MoveDown"), new FontIcon { Glyph = "\uE70D" }, moveDown, row.CanMoveDown, row.MoveDownAutomationId);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
        Add(UiText.Get("TunnelListItem_Remove"), new SymbolIcon(Symbol.Delete), remove, row.CanEdit, row.DeleteAutomationId);
        return menu;

        void Add(string text, IconElement icon, RoutedEventHandler handler, bool enabled = true, string? automationId = null)
        {
            var item = new MenuFlyoutItem { Text = text, Icon = icon, Tag = row, IsEnabled = enabled };
            item.Click += handler;
            if (automationId is not null) { AutomationProperties.SetAutomationId(item, automationId); }
            menu.Items.Add(item);
        }
    }
}

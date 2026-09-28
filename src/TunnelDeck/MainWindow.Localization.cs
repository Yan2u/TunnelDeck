using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace TunnelDeck;

public sealed partial class MainWindow
{
    public void RefreshLanguage()
    {
        if (_closed) { return; }
        UpdateSearchPlaceholder(LayoutRoot.ActualWidth);
        RefreshNativeLabels();
        StorageNotice.Title = UiText.Translate(StorageNotice.Title);
        StorageNotice.Message = UiText.Translate(StorageNotice.Message);
        if (PreviewSelector.Content is string preview) { PreviewSelector.Content = UiText.Translate(preview); }
    }

    private void UpdateSearchPlaceholder(double width)
    {
        GlobalSearch.PlaceholderText = UiText.Get(width >= 1100 ? "MainWindow_SearchProfilesAndServers" : "MainWindowxaml_Search");
        SetLabel(GlobalSearch, "TextBox", UiText.Get("MainWindow_SearchProfilesAndServers"));
    }

    private void RefreshNativeLabels()
    {
        if (_closed) { return; }
        SetLabel(AppTitleBar, "PART_BackButton", UiText.Get("Shell_Back"));
        SetLabel(AppTitleBar, "PART_PaneToggleButton", UiText.Get("Shell_Navigation"));
        SetLabel(GlobalSearch, "TextBox", UiText.Get("MainWindow_SearchProfilesAndServers"));
    }

    private static void SetLabel(DependencyObject parent, string name, string text)
    {
        if (parent is FrameworkElement element && element.Name == name)
        {
            AutomationProperties.SetName(element, text);
            ToolTipService.SetToolTip(element, text);
            return;
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        { SetLabel(VisualTreeHelper.GetChild(parent, index), name, text); }
    }
}

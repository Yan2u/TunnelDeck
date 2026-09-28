using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using TunnelDeck.Core.Navigation;
using TunnelDeck.Core.Transfer;
using TunnelDeck.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TunnelDeck.Pages;

public sealed partial class ExportPage : ShellPage
{
    public ExportViewModel ViewModel { get; } = new();
    public ExportPage() => InitializeComponent();
    protected override void InitializePage()
    {
        ViewModel.Load(Shell, Route == AppRoute.ExportProfiles ? ExportScope.Profiles : ExportScope.Servers);
        Bindings.Update();
    }
    protected override void Refresh() => ViewModel.Refresh();
    protected override void OnAvailabilityChanged() => ViewModel.RefreshAvailability();
    protected override void OnNavigatedFrom(NavigationEventArgs args) { ViewModel.Unload(); base.OnNavigatedFrom(args); }
    private async void OnExportClick(object sender, RoutedEventArgs args) => await ViewModel.ExportAsync(async () =>
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = $"TunnelDeck-{DateTime.Today:yyyy-MM-dd}" };
        InitializeWithWindow.Initialize(picker, Shell.WindowHandle);
        picker.FileTypeChoices.Add(UiText.Get("ExportPagexaml_TunnelDeckExport"), new List<string> { ".json" });
        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    });
    private void OnBackClick(object sender, RoutedEventArgs args) { if (Shell.CanGoBack) { Shell.GoBack(); } else { Shell.Navigate(Route.Parent); } }
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs args) { if (PageContent is not null) { PageContent.Padding = PagePadding(args.NewSize.Width); } }
}

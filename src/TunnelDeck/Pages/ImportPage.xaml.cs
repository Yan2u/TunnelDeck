using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using TunnelDeck.ViewModels;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace TunnelDeck.Pages;

public sealed partial class ImportPage : ShellPage
{
    public ImportViewModel ViewModel { get; } = new();
    public ImportPage() => InitializeComponent();
    protected override void InitializePage() { ViewModel.Load(Shell); Bindings.Update(); FocusInitialControl(ImportFileInput); }
    protected override void Refresh() => ViewModel.RefreshAvailability();
    protected override void OnAvailabilityChanged() => ViewModel.RefreshAvailability();
    protected override void OnNavigatedFrom(NavigationEventArgs args) { ViewModel.Unload(); base.OnNavigatedFrom(args); }
    private void OnRetryPreviewClick(object sender, RoutedEventArgs args) => ViewModel.ReloadPreview();
    private void OnFileKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || args.KeyStatus.WasKeyDown) { return; }
        args.Handled = true;
        ViewModel.ReloadPreview();
    }
    private async void OnImportClick(object sender, RoutedEventArgs args) => await ViewModel.ImportAsync();
    private void OnCancelClick(object sender, RoutedEventArgs args) { if (Shell.CanGoBack) { Shell.GoBack(); } else { Shell.Navigate(Route.Parent); } }
    private async void OnBrowseClick(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.CanChooseFile) { return; }
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            InitializeWithWindow.Initialize(picker, Shell.WindowHandle);
            picker.FileTypeFilter.Add(ViewModel.SourceIndex == 0 ? ".json" : "*");
            var file = await picker.PickSingleFileAsync();
            if (file is not null && ViewModel.CanChooseFile) { ViewModel.SelectFile(file.Path); }
        }
        catch (Exception) { if (ViewModel.CanChooseFile) { ViewModel.ReportPickerError(); } }
    }
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (PageContent is null) { return; }
        PageContent.Padding = PagePadding(args.NewSize.Width);
        ImportFormScroll.MaxHeight = Math.Max(0, args.NewSize.Height * 0.4);
    }
}

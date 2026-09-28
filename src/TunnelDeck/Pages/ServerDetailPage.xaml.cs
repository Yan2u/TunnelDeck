using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using TunnelDeck.ViewModels;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace TunnelDeck.Pages;

public sealed partial class ServerDetailPage : ShellPage
{
    public ServerDetailViewModel ViewModel { get; } = new();
    public ServerDetailPage() => InitializeComponent();
    protected override void InitializePage()
    {
        ViewModel.Load(Shell, Route.ResourceId);
        Bindings.Update();
        if (Route.ResourceId is null) { FocusInitialControl(DisplayNameInput); }
    }
    protected override void Refresh() => ViewModel.RefreshAvailability();
    protected override void OnAvailabilityChanged() => ViewModel.RefreshAvailability();
    protected override void OnNavigatedFrom(NavigationEventArgs args) { ViewModel.Unload(); base.OnNavigatedFrom(args); }
    private async void OnSaveClick(object sender, RoutedEventArgs args) => await ViewModel.SaveAsync();
    private async void OnTestClick(object sender, RoutedEventArgs args) => await ViewModel.TestOrCancelAsync();
    private async void OnDeleteClick(object sender, RoutedEventArgs args) => await ViewModel.DeleteAsync();
    private async void OnTerminalClick(object sender, RoutedEventArgs args) => await ViewModel.OpenTerminalAsync();
    private async void OnBrowseClick(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.CanEditIdentity) { return; }
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            InitializeWithWindow.Initialize(picker, Shell.WindowHandle);
            picker.FileTypeFilter.Add("*");
            var file = await picker.PickSingleFileAsync();
            if (file is not null && ViewModel.IsActive && ViewModel.CanEditIdentity) { ViewModel.IdentityFilePath = file.Path; }
        }
        catch (Exception) { if (ViewModel.IsActive) { ViewModel.ReportKeyPickerError(); } }
    }
    private void OnCancelClick(object sender, RoutedEventArgs args) { if (Shell.CanGoBack) { Shell.GoBack(); } else { Shell.Navigate(Route.Parent); } }
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs args) { if (PageContent is not null) { PageContent.Padding = PagePadding(args.NewSize.Width); } }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage.Pickers;
using Windows.System;
using WinRT.Interop;

namespace TunnelDeck.Pages;

public sealed partial class SettingsPage
{
    private async void OnDefaultAuthenticationChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_refreshing || Shell is null || !ViewModel.CanEdit) { return; }
        await ViewModel.SetDefaultAuthenticationAsync(DefaultAuthentication.SelectedIndex);
        Refresh();
    }

    private async void OnSaveDefaultKey(object sender, RoutedEventArgs args) { await ViewModel.SaveDefaultKeyAsync(); Refresh(); }

    private async void OnDefaultKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || args.KeyStatus.WasKeyDown) { return; }
        args.Handled = true;
        await ViewModel.SaveDefaultKeyAsync();
        Refresh();
    }

    private async void OnBrowseDefaultKey(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.CanEdit) { return; }
        try
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            InitializeWithWindow.Initialize(picker, Shell.WindowHandle);
            picker.FileTypeFilter.Add("*");
            var file = await picker.PickSingleFileAsync();
            if (file is not null && ViewModel.IsActive && ViewModel.CanEdit)
            {
                ViewModel.DefaultIdentityFilePath = file.Path;
                await ViewModel.SaveDefaultKeyAsync();
                Refresh();
            }
        }
        catch (Exception) { if (ViewModel.IsActive) { ViewModel.ReportKeyPickerError(); } }
    }
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Navigation;
using TunnelDeck.Core.Navigation;
using TunnelDeck.Core.Presentation;
using TunnelDeck.ViewModels;

namespace TunnelDeck.Pages;

public sealed partial class ProfileDetailPage : ShellPage
{
    public ProfileDetailViewModel ViewModel { get; } = new();
    public ProfileDetailPage() => InitializeComponent();
    protected override void InitializePage() { ViewModel.Load(Shell, Route.ResourceId); Bindings.Update(); }
    protected override void Refresh() => ViewModel.Update();
    protected override void OnAvailabilityChanged() => ViewModel.Update();
    protected override void OnNavigatedFrom(NavigationEventArgs args) { ViewModel.Dispose(); base.OnNavigatedFrom(args); }
    private async void OnSaveClick(object sender, RoutedEventArgs args) => await ViewModel.SaveAsync();
    private async void OnDeleteClick(object sender, RoutedEventArgs args) => await ViewModel.DeleteAsync();
    private void OnCancelClick(object sender, RoutedEventArgs args) { ViewModel.Editor.Discard(); if (Shell.CanGoBack) { Shell.GoBack(); } else { Shell.Navigate(Route.Parent); } }
    private async void OnStartAll(object sender, RoutedEventArgs args) => await ViewModel.StartAllAsync();
    private async void OnRetryFailed(object sender, RoutedEventArgs args) => await ViewModel.RetryFailedAsync();
    private async void OnCancelStart(object sender, RoutedEventArgs args) => await ViewModel.CancelStartAsync();
    private async void OnRetryActivity(object sender, RoutedEventArgs args) => await Shell.Forwarding.RetryActivityAsync();
    private void OnShowTunnels(object sender, RoutedEventArgs args) => Shell.Navigate(AppRoute.Dashboard);
    private void OnAddRule(object sender, RoutedEventArgs args) { ViewModel.Editor.AddRule(Shell.Quick.SelectedServerId); FocusRuleEditor(); }
    private void OnEditRule(object sender, RoutedEventArgs args) { if ((sender as FrameworkElement)?.Tag is ProfileRuleItem item) { ViewModel.Editor.EditRule(item.Id); FocusRuleEditor(); } }
    private void FocusRuleEditor() => DispatcherQueue.TryEnqueue(() =>
    {
        if (!IsLoaded || !ViewModel.Editor.IsEditingRule) { return; }
        DetailScroll.UpdateLayout();
        RuleEditor.FocusEditor();
        double offset = DetailScroll.VerticalOffset + RuleEditorPanel.TransformToVisual(DetailScroll).TransformPoint(new(0, 0)).Y;
        DetailScroll.ChangeView(null, Math.Clamp(offset, 0, DetailScroll.ScrollableHeight), null, true);
    });
    private void OnApplyRule(object? sender, EventArgs args) { if (ViewModel.Editor.ApplyRule()) { AddRuleButton.Focus(FocusState.Keyboard); } }
    private void OnCancelRule(object? sender, EventArgs args) { ViewModel.Editor.CancelRule(); AddRuleButton.Focus(FocusState.Keyboard); }
    private async void OnStartRule(object sender, RoutedEventArgs args) { if ((sender as FrameworkElement)?.Tag is ProfileRuleItem item) { await ViewModel.StartRuleAsync(item); } }
    private async void OnStopRule(object sender, RoutedEventArgs args) { if ((sender as FrameworkElement)?.Tag is ProfileRuleItem item) { await ViewModel.StopRuleAsync(item); } }
    private void OnMoveRuleUp(object sender, RoutedEventArgs args) { if ((sender as FrameworkElement)?.Tag is ProfileRuleItem item) { ViewModel.Editor.MoveRule(item.Id, -1); } }
    private void OnMoveRuleDown(object sender, RoutedEventArgs args) { if ((sender as FrameworkElement)?.Tag is ProfileRuleItem item) { ViewModel.Editor.MoveRule(item.Id, 1); } }
    private void OnRemoveRule(object sender, RoutedEventArgs args) { if ((sender as FrameworkElement)?.Tag is ProfileRuleItem item) { ViewModel.Editor.RemoveRule(item.Id); } }
    private void OnPageSizeChanged(object sender, SizeChangedEventArgs args) { if (PageContent is not null) { PageContent.Padding = PagePadding(args.NewSize.Width); } }
}

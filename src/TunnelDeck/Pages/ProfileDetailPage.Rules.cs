using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.Pages;

public sealed partial class ProfileDetailPage
{
    private readonly CancellationTokenSource _ruleDialogLifetime = new();

    private async void OnAddRule(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.Editor.CanEdit) { return; }
        ViewModel.Editor.AddRule(Shell.Quick.SelectedServerId);
        await ShowRuleDialogAsync(sender as Control);
    }

    private async void OnEditRule(object sender, RoutedEventArgs args)
    {
        if (!ViewModel.Editor.CanEdit || sender is not Control { Tag: ProfileRuleItem item } owner) { return; }
        ViewModel.Editor.EditRule(item.Id);
        await ShowRuleDialogAsync(owner);
    }

    private async Task ShowRuleDialogAsync(Control? owner)
    {
        if (!ViewModel.Editor.IsEditingRule) { return; }
        try { await Shell.ShowRuleEditorAsync(ViewModel.Editor, _ruleDialogLifetime.Token); }
        catch (OperationCanceledException) { }
        finally
        {
            // Escape, navigation and window shutdown all discard only the open rule draft.
            if (ViewModel.Editor.IsEditingRule) { ViewModel.Editor.CancelRule(); }
            if (IsLoaded && AddRuleButton.IsEnabled)
            {
                Control target = owner is { IsLoaded: true, IsEnabled: true } ? owner : AddRuleButton;
                target.Focus(FocusState.Keyboard);
            }
        }
    }

    private void CancelRuleDialog()
    {
        _ruleDialogLifetime.Cancel();
        _ruleDialogLifetime.Dispose();
    }
}

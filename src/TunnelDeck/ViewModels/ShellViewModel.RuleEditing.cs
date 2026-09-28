using TunnelDeck.Controls;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.ViewModels;

public sealed partial class ShellViewModel
{
    public Task ShowRuleEditorAsync(ProfileEditorModel editor, CancellationToken cancellationToken) =>
        _services.Dialogs.ShowAsync(() => new RuleEditorDialog(editor, this), cancellationToken);
}

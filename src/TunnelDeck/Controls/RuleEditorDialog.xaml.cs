using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Core.Presentation;
using TunnelDeck.ViewModels;

namespace TunnelDeck.Controls;

public sealed partial class RuleEditorDialog : ContentDialog
{
    private XamlRoot? _dialogRoot;
    public ProfileEditorModel Editor { get; }
    public ShellViewModel Shell { get; }

    public RuleEditorDialog(ProfileEditorModel editor, ShellViewModel shell)
    {
        Editor = editor;
        Shell = shell;
        InitializeComponent();
        // A popup can raise Opened before its Loading bindings run. Populate the
        // editor model before Opened restores selection and moves keyboard focus.
        Bindings.Update();
    }

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args)
    {
        _dialogRoot = XamlRoot;
        _dialogRoot.Changed += OnRootChanged;
        ResizeBody();
        if (GetTemplateChild("PrimaryButton") is Button apply) { AutomationProperties.SetAutomationId(apply, "ApplyRule"); }
        if (GetTemplateChild("CloseButton") is Button cancel) { AutomationProperties.SetAutomationId(cancel, "CancelRule"); }
        RuleEditor.FocusEditor();
    }

    private void OnPrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args) => args.Cancel = !TryApplyRule();

    private void OnApplyRequested(object? sender, EventArgs args)
    {
        if (TryApplyRule()) { Hide(); }
    }

    private bool TryApplyRule()
    {
        RuleEditor.CommitPendingInput();
        if (Editor.ApplyRule()) { return true; }
        RuleEditor.FocusValidationError();
        return false;
    }

    private void OnRootChanged(XamlRoot sender, XamlRootChangedEventArgs args) => ResizeBody();
    private void ResizeBody() => BodyScroll.MaxHeight = Math.Max(120, XamlRoot.Size.Height - 240);

    private void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        if (_dialogRoot is null) { return; }
        _dialogRoot.Changed -= OnRootChanged;
        _dialogRoot = null;
    }
}

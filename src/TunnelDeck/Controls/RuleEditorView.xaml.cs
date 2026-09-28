using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Core.Presentation;
using TunnelDeck.ViewModels;

namespace TunnelDeck.Controls;

public sealed partial class RuleEditorView : UserControl
{
    public static readonly DependencyProperty ModelProperty = DependencyProperty.Register(nameof(Model), typeof(RuleEditorModel), typeof(RuleEditorView), new PropertyMetadata(null, OnModelChanged));
    public static readonly DependencyProperty ServerChoicesProperty = DependencyProperty.Register(nameof(ServerChoices), typeof(IReadOnlyList<ResourceRow>), typeof(RuleEditorView), new PropertyMetadata(null, OnChoicesChanged));
    public RuleEditorModel Model { get => (RuleEditorModel)GetValue(ModelProperty); set => SetValue(ModelProperty, value); }
    public IReadOnlyList<ResourceRow> ServerChoices { get => (IReadOnlyList<ResourceRow>)GetValue(ServerChoicesProperty); set => SetValue(ServerChoicesProperty, value); }
    public event EventHandler? ApplyRequested;
    private bool _selecting;

    public RuleEditorView()
    {
        InitializeComponent();
        Loaded += (_, _) => { if (Model is not null) { Model.PropertyChanged -= OnModelPropertyChanged; Model.PropertyChanged += OnModelPropertyChanged; } RefreshSelection(); };
        Unloaded += (_, _) => { if (Model is not null) { Model.PropertyChanged -= OnModelPropertyChanged; } };
    }
    private static void OnModelChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var view = (RuleEditorView)sender;
        if (args.OldValue is RuleEditorModel before) { before.PropertyChanged -= view.OnModelPropertyChanged; }
        if (view.IsLoaded && args.NewValue is RuleEditorModel after) { after.PropertyChanged += view.OnModelPropertyChanged; }
        view.Bindings.Update(); view.RefreshSelection();
    }
    private static void OnChoicesChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((RuleEditorView)sender).RefreshSelection();
    private void OnModelPropertyChanged(object? sender, PropertyChangedEventArgs args) { if (args.PropertyName == nameof(RuleEditorModel.SelectedServerId)) { RefreshSelection(); } }
    private void RefreshSelection()
    {
        if (ServerSelector is null || Model is null) { return; }
        _selecting = true;
        try { ServerSelector.SelectedItem = ServerChoices?.FirstOrDefault(server => server.Id == Model.SelectedServerId); }
        finally { _selecting = false; }
    }
    private void OnServerSelected(object sender, SelectionChangedEventArgs args)
    { if (!_selecting && Model is not null && ServerSelector.SelectedItem is ResourceRow server) { Model.SelectedServerId = server.Id; } }
    public void FocusEditor()
    {
        RefreshSelection();
        if (Model.SelectedServerId is null) { ServerSelector.Focus(FocusState.Keyboard); }
        else { LocalPort.FocusInput(selectAll: true); }
    }
    public void CommitPendingInput()
    {
        Model.LocalPortText = LocalPort.RawText; Model.RemotePortText = RemotePort.RawText;
    }

    public void FocusValidationError()
    {
        if (Model.ServerError.Length > 0) { ServerSelector.Focus(FocusState.Keyboard); }
        else if (Model.LocalHostError.Length > 0) { LocalHostInput.Focus(FocusState.Keyboard); }
        else if (Model.LocalPortError.Length > 0) { LocalPort.FocusInput(selectAll: true); }
        else if (Model.RemoteHostError.Length > 0) { RemoteHostInput.Focus(FocusState.Keyboard); }
        else if (Model.RemotePortError.Length > 0) { RemotePort.FocusInput(selectAll: true); }
    }

    private void OnPortSubmitted(object? sender, EventArgs args) => ApplyRequested?.Invoke(this, EventArgs.Empty);
}

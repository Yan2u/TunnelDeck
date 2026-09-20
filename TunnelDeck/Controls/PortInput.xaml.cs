using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace TunnelDeck.Controls;

/// <summary>Composes a native NumberBox while exposing its uncommitted text, rather than its previous numeric Value.</summary>
public sealed partial class PortInput : UserControl
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(PortInput), new PropertyMetadata(string.Empty, OnTextChanged));
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(nameof(Header), typeof(string), typeof(PortInput), new PropertyMetadata("Port"));
    private TextBox? _textBox;
    private bool _fromEditor, _updating;
    public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
    public string Header { get => (string)GetValue(HeaderProperty); set => SetValue(HeaderProperty, value); }
    public string RawText => _textBox?.Text ?? Text;
    public event EventHandler? Submitted;

    public PortInput()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Editor.AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown), true);
    }

    private void OnLoaded(object sender, RoutedEventArgs args)
    {
        Editor.ApplyTemplate();
        _textBox = FindTextBox(Editor);
        if (_textBox is null) { return; }
        AutomationProperties.SetAutomationId(_textBox, AutomationProperties.GetAutomationId(this) + "Text");
        AutomationProperties.SetName(_textBox, Header);
        UpdateEditor();
        _textBox.TextChanged += OnRawTextChanged;
    }
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        if (_textBox is not null) { _textBox.TextChanged -= OnRawTextChanged; _textBox = null; }
    }
    private static TextBox? FindTextBox(DependencyObject element)
    {
        if (element is TextBox textBox) { return textBox; }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(element); index++)
        { if (FindTextBox(VisualTreeHelper.GetChild(element, index)) is { } found) { return found; } }
        return null;
    }
    private static void OnTextChanged(DependencyObject source, DependencyPropertyChangedEventArgs args) => ((PortInput)source).UpdateEditor();
    private void UpdateEditor()
    {
        if (_fromEditor || _textBox is null || _textBox.Text == Text) { return; }
        _updating = true;
        try { _textBox.Text = Text; }
        finally { _updating = false; }
    }
    private void OnRawTextChanged(object sender, TextChangedEventArgs args)
    {
        if (_updating) { return; }
        _fromEditor = true;
        try { Text = _textBox!.Text; }
        finally { _fromEditor = false; }
    }
    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key != VirtualKey.Enter || args.KeyStatus.WasKeyDown) { return; }
        args.Handled = true;
        Submitted?.Invoke(this, EventArgs.Empty);
    }
    public void FocusInput(bool selectAll = false)
    {
        if (_textBox is null) { Editor.Focus(FocusState.Keyboard); return; }
        _textBox.Focus(FocusState.Keyboard);
        if (selectAll) { _textBox.SelectAll(); }
    }
}

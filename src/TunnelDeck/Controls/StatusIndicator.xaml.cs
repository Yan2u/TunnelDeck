using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TunnelDeck.Controls;

public sealed partial class StatusIndicator : UserControl
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(string), typeof(StatusIndicator), new PropertyMetadata(string.Empty, OnStateChanged));
    public static readonly DependencyProperty ShowLabelProperty = DependencyProperty.Register(nameof(ShowLabel), typeof(bool), typeof(StatusIndicator), new PropertyMetadata(true));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(StatusIndicator), new PropertyMetadata(string.Empty));
    public string State { get => (string)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public bool ShowLabel { get => (bool)GetValue(ShowLabelProperty); set => SetValue(ShowLabelProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public StatusIndicator() { InitializeComponent(); Loaded += (_, _) => UpdateState(); }
    private static void OnStateChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) => ((StatusIndicator)sender).UpdateState();
    private void UpdateState() => VisualStateManager.GoToState(this, State switch
    {
        "Running" or "Started" => "Positive",
        "Failed" => "Critical",
        "Starting" or "Stopping" or "Reconnecting" => "Attention",
        _ => "Neutral",
    }, false);
    public string Glyph(string state) => state switch
    {
        "Running" or "Started" => "\uE73E",
        "Failed" => "\uEA39",
        "Starting" or "Stopping" or "Reconnecting" => "\uE72C",
        "Cancelled" => "\uE711",
        _ => "\uE71A",
    };
}

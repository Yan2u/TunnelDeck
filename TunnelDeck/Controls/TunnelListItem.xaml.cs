using System.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.Controls;

public sealed partial class TunnelListItem : UserControl
{
    public static readonly DependencyProperty ItemProperty = DependencyProperty.Register(nameof(Item), typeof(TunnelItem), typeof(TunnelListItem), new PropertyMetadata(null, OnItemChanged));
    public TunnelItem Item { get => (TunnelItem)GetValue(ItemProperty); set => SetValue(ItemProperty, value); }
    public event EventHandler<TunnelItem>? StopRequested;
    public event EventHandler<TunnelItem>? RetryRequested;
    public event EventHandler<TunnelItem>? RemoveRequested;
    public event EventHandler<TunnelItem>? DetailsRefreshRequested;
    public event EventHandler<TunnelItem>? OpenServerRequested;
    public event EventHandler<TunnelItem>? OpenProfileRequested;
    private DispatcherQueueTimer? _timer;
    private TunnelItem? _observed;
    private bool _loaded, _detailsOpen;
    public TunnelListItem()
    {
        InitializeComponent();
        Loaded += (_, _) => { _loaded = true; ObserveItem(); };
        Unloaded += (_, _) => { _loaded = false; ObserveItem(); };
    }
    private static void OnItemChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var control = (TunnelListItem)sender;
        control.Bindings.Update();
        control.ObserveItem();
    }
    private void ObserveItem()
    {
        if (_observed is not null) { _observed.PropertyChanged -= OnRuntimeChanged; }
        _observed = _loaded ? Item : null;
        if (_observed is not null) { _observed.PropertyChanged += OnRuntimeChanged; }
        UpdateTimer();
    }
    private void OnRuntimeChanged(object? sender, PropertyChangedEventArgs args) => UpdateTimer();
    private void OnStop(object sender, RoutedEventArgs args) => StopRequested?.Invoke(this, Item);
    private void OnRetry(object sender, RoutedEventArgs args) => RetryRequested?.Invoke(this, Item);
    private void OnRemove(object sender, RoutedEventArgs args) => RemoveRequested?.Invoke(this, Item);
    private void OnOpenServer(object sender, RoutedEventArgs args) { DetailsFlyout.Hide(); OpenServerRequested?.Invoke(this, Item); }
    private void OnOpenProfile(object sender, RoutedEventArgs args) { DetailsFlyout.Hide(); OpenProfileRequested?.Invoke(this, Item); }
    private void OnDetailsOpened(object? sender, object args)
    {
        _detailsOpen = true;
        DetailsRefreshRequested?.Invoke(this, Item);
        UpdateTimer();
    }
    private void UpdateTimer()
    {
        if (!_loaded || (!_detailsOpen && _observed?.IsReconnecting != true)) { StopTimer(); return; }
        _timer ??= DispatcherQueue.CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick -= OnDetailsTick;
        _timer.Tick += OnDetailsTick;
        if (!_timer.IsRunning) { _timer.Start(); }
    }
    private void OnDetailsTick(DispatcherQueueTimer sender, object args) => DetailsRefreshRequested?.Invoke(this, Item);
    private void OnDetailsClosed(object? sender, object args) { _detailsOpen = false; UpdateTimer(); }
    private void StopTimer() { if (_timer is not null) { _timer.Stop(); _timer.Tick -= OnDetailsTick; } }
}

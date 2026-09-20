using System.Globalization;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Navigation;
using TunnelDeck.Core.Runtime;

namespace TunnelDeck.Core.Presentation;

/// <summary>Stable row identity; passively applying a runtime snapshot never replaces a focused row.</summary>
public sealed class TunnelItem : ObservableModel
{
    private ActiveTunnel _value;
    private AppState _configuration;
    private DateTimeOffset _now;
    private bool _busy;
    public bool IsPreview { get; }
    public Guid Id => _value.Id;
    public ActiveTunnel Value => _value;
    public TunnelItem(ActiveTunnel value, AppState configuration, DateTimeOffset now, bool preview = false)
    { _value = value; _configuration = configuration; _now = now; IsPreview = preview; }
    public string ServerName => _value.Source.ServerName;
    public string Status => _value.State.ToString();
    public bool IsReconnecting => _value.State == TunnelState.Reconnecting;
    public bool IsBusy { get => _busy; internal set { if (SetProperty(ref _busy, value)) { Changed(); } } }
    public bool IsPending => _value.State is TunnelState.Starting or TunnelState.Stopping or TunnelState.Reconnecting;
    public bool CanStop => !IsPreview && !IsBusy && _value.State is TunnelState.Starting or TunnelState.Running or TunnelState.Stopping or TunnelState.Reconnecting;
    public bool CanRetry => !IsPreview && !IsBusy && CanOpenServer && _value.State is TunnelState.Failed or TunnelState.Cancelled;
    public bool CanRemove => !IsPreview && !IsBusy && _value.State is TunnelState.Failed or TunnelState.Cancelled or TunnelState.Stopped;
    public string Mode => ResourceDisplay.ModeName(_value.Requested.Mode);
    public string Route => ResourceDisplay.PortRoute(_value.Requested, _value.Actual);
    public string FullRoute => _value.Actual is null ? "Not established" : ResourceDisplay.FullRoute(_value.Requested, _value.Actual);
    public string RequestedRoute => ResourceDisplay.FullRoute(_value.Requested);
    public string LeadingEndpoint => _value.Requested.Mode == ForwardMode.Local ? $"Local :{(_value.Actual?.Local.Port ?? _value.Requested.LocalPort)}" : $"Remote :{(_value.Actual?.Remote.Port ?? _value.Requested.RemotePort)}";
    public string TrailingEndpoint => _value.Requested.Mode == ForwardMode.Local ? $"→ Remote :{(_value.Actual?.Remote.Port ?? _value.Requested.RemotePort)}" : $"→ Local :{(_value.Actual?.Local.Port ?? _value.Requested.LocalPort)}";
    public bool PortAdjusted => _value.Requested.Mode == ForwardMode.Local && _value.Actual is not null && _value.Actual.Local.Port != _value.Requested.LocalPort;
    public string Adjustment => IsReconnecting
        ? $"Local port {_value.Requested.LocalPort} was already in use. The previous listener used {_value.Actual?.Local.Port}; recovery will try that port first."
        : $"Local port {_value.Requested.LocalPort} was already in use. Listening on {_value.Actual?.Local.Port} instead. The target port and saved rule are unchanged.";
    public string Error => IsReconnecting ? string.Empty : ConnectionError;
    public string ConnectionError => _value.LastError?.Message ?? string.Empty;
    public string ActualLabel => IsReconnecting ? "Previous listener" : "Actual";
    public string ReconnectMessage => !IsReconnecting ? string.Empty : _value.NextReconnectAt is DateTimeOffset next
        ? $"Forward is offline. Retry in {Math.Max(0, Math.Ceiling((next - _now).TotalSeconds)):0} s · attempt {_value.ReconnectAttempt}. Stop cancels recovery."
        : _value.ReconnectAttempt > 0 ? $"Restoring the forward · attempt {_value.ReconnectAttempt}. Stop cancels recovery."
        : "Connection lost. Preparing to reconnect. Stop cancels recovery.";
    public string ReconnectSummary => $"Automatic recoveries: {_value.ReconnectCount:N0}." +
        (_value.ReconnectCount > 0 && _value.LastConnectionAt is DateTimeOffset restored ? $" Last restored: {restored.ToLocalTime():G}." : string.Empty);
    public string RequestError => _value.LastRequestError?.Message ?? string.Empty;
    public string RequestSummary => $"Observed requests: {_value.AcceptedConnections:N0}; observed failures: {_value.RequestFailureCount:N0}.";
    public string Started => _value.StartedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "Not started";
    public string Duration => FormatDuration(_value.StartedAt, _value.EndedAt ?? _now);
    public string Source => _value.Source.ProfileName ?? "Quick forward";
    public bool CanOpenServer => _configuration.Servers.Any(server => server.Id == _value.ServerId);
    public bool CanOpenProfile => _value.Source.ProfileId is Guid id && _configuration.Profiles.Any(profile => profile.Id == id);
    public AppRoute ServerRoute => AppRoute.Server(_value.ServerId);
    public AppRoute ProfileRoute => AppRoute.Profile(_value.Source.ProfileId);
    public string AccessibleName => $"{ServerName}, {Mode}, {Route}, {Status}";
    public string AutomationId => $"Tunnel_{Id:N}";
    public string StopId => $"StopTunnel_{Id:N}";
    public string RetryId => $"RetryTunnel_{Id:N}";
    public string MoreId => $"MoreTunnel_{Id:N}";
    public string DetailsId => $"TunnelDetails_{Id:N}";
    public string AdjustmentId => $"PortAdjustment_{Id:N}";
    public string ReconnectId => $"Reconnect_{Id:N}";
    public string StopName => $"Stop {ServerName}, {Route}";
    public string DetailsName => $"Details for {ServerName}, {Route}";
    public override string ToString() => AccessibleName;
    public void Update(ActiveTunnel value, AppState configuration) { _value = value; _configuration = configuration; Changed(); }
    public void RefreshTime(DateTimeOffset now) { _now = now; OnPropertyChanged(nameof(Duration)); OnPropertyChanged(nameof(ReconnectMessage)); }
    private void Changed() => OnPropertyChanged(string.Empty);
    public static string FormatDuration(DateTimeOffset? started, DateTimeOffset now)
    {
        if (started is null) { return "—"; }
        TimeSpan elapsed = now - started.Value;
        if (elapsed < TimeSpan.Zero) { elapsed = TimeSpan.Zero; }
        return elapsed.TotalDays >= 1 ? $"{(int)elapsed.TotalDays}d {elapsed.Hours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}" : $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
}

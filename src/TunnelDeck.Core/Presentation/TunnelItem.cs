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
    public string Status => UiText.State(_value.State);
    public string StatusKey => _value.State.ToString();
    public bool IsReconnecting => _value.State == TunnelState.Reconnecting;
    public bool IsBusy { get => _busy; internal set { if (SetProperty(ref _busy, value)) { Changed(); } } }
    public bool IsPending => _value.State is TunnelState.Starting or TunnelState.Stopping or TunnelState.Reconnecting;
    public bool CanStop => !IsPreview && !IsBusy && _value.State is TunnelState.Starting or TunnelState.Running or TunnelState.Stopping or TunnelState.Reconnecting;
    public bool CanRetry => !IsPreview && !IsBusy && CanOpenServer && _value.State is TunnelState.Failed or TunnelState.Cancelled;
    public bool CanRemove => !IsPreview && !IsBusy && _value.State is TunnelState.Failed or TunnelState.Cancelled or TunnelState.Stopped;
    public string Mode => ResourceDisplay.ModeName(_value.Requested.Mode);
    public string Route => ResourceDisplay.PortRoute(_value.Requested, _value.Actual);
    public string FullRoute => _value.Actual is null ? UiText.Get("TunnelItem_NotEstablished") : ResourceDisplay.FullRoute(_value.Requested, _value.Actual);
    public string RequestedRoute => ResourceDisplay.FullRoute(_value.Requested);
    public string LeadingEndpoint => _value.Requested.Mode == ForwardMode.Local ? UiText.Get("TunnelItem_Local", (_value.Actual?.Local.Port ?? _value.Requested.LocalPort)) : UiText.Get("TunnelItem_Remote", (_value.Actual?.Remote.Port ?? _value.Requested.RemotePort));
    public string TrailingEndpoint => _value.Requested.Mode == ForwardMode.Local ? UiText.Get("TunnelItem_Remote_412", (_value.Actual?.Remote.Port ?? _value.Requested.RemotePort)) : UiText.Get("TunnelItem_Local_413", (_value.Actual?.Local.Port ?? _value.Requested.LocalPort));
    public bool PortAdjusted => _value.Requested.Mode == ForwardMode.Local && _value.Actual is not null && _value.Actual.Local.Port != _value.Requested.LocalPort;
    public string Adjustment => IsReconnecting
        ? UiText.Get("TunnelItem_LocalPortWasAlreadyInUseThePrevious", _value.Requested.LocalPort, _value.Actual?.Local.Port)
        : UiText.Get("TunnelItem_LocalPortWasAlreadyInUseListeningOn", _value.Requested.LocalPort, _value.Actual?.Local.Port);
    public string Error => IsReconnecting ? string.Empty : ConnectionError;
    public string ConnectionError => UiText.Translate(_value.LastError?.Message);
    public string ActualLabel => IsReconnecting ? UiText.Get("TunnelItem_PreviousListener") : UiText.Get("TunnelItem_Actual");
    public string ReconnectMessage => !IsReconnecting ? string.Empty : _value.NextReconnectAt is DateTimeOffset next
        ? UiText.Get("TunnelItem_ForwardIsOfflineRetryInSAttemptStop", Math.Max(0, Math.Ceiling((next - _now).TotalSeconds)), _value.ReconnectAttempt)
        : _value.ReconnectAttempt > 0 ? UiText.Get("TunnelItem_RestoringTheForwardAttemptStopCancelsRecovery", _value.ReconnectAttempt)
        : UiText.Get("TunnelItem_ConnectionLostPreparingToReconnectStopCancelsRecovery");
    public string ReconnectSummary => UiText.Get("TunnelItem_AutomaticRecoveries", _value.ReconnectCount) +
        (_value.ReconnectCount > 0 && _value.LastConnectionAt is DateTimeOffset restored ? UiText.Get("TunnelItem_LastRestored", restored.ToLocalTime()) : string.Empty);
    public string RequestError => UiText.Translate(_value.LastRequestError?.Message);
    public string RequestSummary => UiText.Get("TunnelItem_ObservedRequestsObservedFailures", _value.AcceptedConnections, _value.RequestFailureCount);
    public string Started => _value.StartedAt?.ToLocalTime().ToString("g", UiText.Culture) ?? UiText.Get("TunnelItem_NotStarted");
    public string Duration => FormatDuration(_value.StartedAt, _value.EndedAt ?? _now);
    public string Source => _value.Source.ProfileName ?? UiText.Get("DashboardPage_QuickForward");
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
    public string StopName => UiText.Get("TunnelItem_Stop", ServerName, Route);
    public string DetailsName => UiText.Get("TunnelItem_DetailsFor", ServerName, Route);
    public override string ToString() => AccessibleName;
    public void Update(ActiveTunnel value, AppState configuration) { _value = value; _configuration = configuration; Changed(); }
    public void RefreshTime(DateTimeOffset now) { _now = now; OnPropertyChanged(nameof(Duration)); OnPropertyChanged(nameof(ReconnectMessage)); }
    private void Changed() => OnPropertyChanged(string.Empty);
    public static string FormatDuration(DateTimeOffset? started, DateTimeOffset now)
    {
        if (started is null) { return "—"; }
        TimeSpan elapsed = now - started.Value;
        if (elapsed < TimeSpan.Zero) { elapsed = TimeSpan.Zero; }
        return elapsed.TotalDays >= 1 ? UiText.Get("Duration_Days", (int)elapsed.TotalDays, elapsed.Hours, elapsed.Minutes, elapsed.Seconds)
            : $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
    }
}

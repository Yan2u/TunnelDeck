using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Profiles;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Presentation;

public sealed class ProfileRuleItem(ForwardRule rule) : ObservableModel
{
    private AppState _state = new();
    private ActiveTunnel? _runtime;
    private ProfileRuleResult? _result;
    private bool _canRun, _canStop, _canEdit;
    private int _index, _count;
    public ForwardRule Value { get; } = rule;
    public Guid Id => Value.Id;
    public Guid? RuntimeId => _runtime?.Id;
    public string ServerName => _state.Servers.FirstOrDefault(server => server.Id == Value.ServerId) is { } server ? ResourceDisplay.ServerName(server) : UiText.Get("ProfileRuleItem_ServerUnavailable");
    public string Mode => ResourceDisplay.ModeName(Value.Mode);
    public string Route => ResourceDisplay.PortRoute(Value);
    public string FullRoute => ResourceDisplay.FullRoute(Value);
    public string PortPolicy => Value.Mode == ForwardMode.Local && Value.AutoFindAvailablePort ? UiText.Get("ProfileRuleItem_FindTheNextLocalPortIfOccupied") : string.Empty;
    public string Status => _runtime?.State == TunnelState.Running ? _result?.Outcome == ProfileRuleOutcome.AlreadyRunning ? UiText.Get("ProfileRuleItem_AlreadyRunning") : UiText.Get("ProfileRuleItem_Running") :
        _runtime is { State: TunnelState.Reconnecting, NextReconnectAt: { } next } ? UiText.Get("ProfileRuleItem_ReconnectingRetryAt", next.ToLocalTime()) :
        _runtime is { } runtime ? UiText.State(runtime.State) : _result?.Outcome switch
        {
            ProfileRuleOutcome.Starting => UiText.Get("ProfileRuleItem_Starting"), ProfileRuleOutcome.Failed => UiText.Get("ProfileRuleItem_Failed"), ProfileRuleOutcome.Cancelled => UiText.Get("ProfileRuleItem_Cancelled"),
            ProfileRuleOutcome.AlreadyReconnecting => UiText.Get("ProfileRuleItem_Reconnecting"), _ => UiText.Get("ProfileRuleItem_Stopped"),
        };
    public string Error => _runtime?.State == TunnelState.Reconnecting ? string.Empty : UiText.Translate(_runtime?.LastError?.Message ?? _result?.Failure?.Message);
    public bool CanStart => _canRun && _runtime?.State is not (TunnelState.Starting or TunnelState.Running or TunnelState.Reconnecting or TunnelState.Stopping);
    public bool CanStop => _canStop && _runtime?.State is TunnelState.Starting or TunnelState.Running or TunnelState.Reconnecting or TunnelState.Stopping;
    public bool ShowStop => _runtime?.State is TunnelState.Starting or TunnelState.Running or TunnelState.Reconnecting or TunnelState.Stopping;
    public bool CanEdit => _canEdit;
    public bool CanMoveUp => _canEdit && _index > 0;
    public bool CanMoveDown => _canEdit && _index + 1 < _count;
    public string AccessibleName => $"{ServerName}, {Mode}, {Route}, {Status}";
    public string RowId => $"ProfileRule_{Id:N}";
    public string StartId => $"StartRule_{Id:N}";
    public string StopId => $"StopRule_{Id:N}";
    public string EditId => $"EditRule_{Id:N}";
    public string MoreId => $"MoreRule_{Id:N}";
    public string MoveUpId => $"MoveUpRule_{Id:N}";
    public string MoveDownId => $"MoveDownRule_{Id:N}";
    public string RemoveId => $"RemoveRule_{Id:N}";

    public void Refresh(AppState state, TunnelManagerSnapshot runtime, ProfileRunSnapshot? run, bool canRun, bool canStop, bool canEdit, int index, int count)
    {
        _state = state; _canRun = canRun; _canStop = canStop; _canEdit = canEdit; _index = index; _count = count;
        _result = run?.Rules.FirstOrDefault(item => item.Launch.Requested == Value);
        ForwardRequestKey key = ForwardRequestKey.FromRule(Value);
        _runtime = runtime.Tunnels.FirstOrDefault(tunnel => ForwardRequestKey.FromRule(tunnel.Requested) == key && tunnel.State is TunnelState.Starting or TunnelState.Running or TunnelState.Stopping or TunnelState.Reconnecting)
            ?? runtime.Tunnels.FirstOrDefault(tunnel => tunnel.Id == _result?.TunnelId);
        OnPropertyChanged(string.Empty);
    }
}

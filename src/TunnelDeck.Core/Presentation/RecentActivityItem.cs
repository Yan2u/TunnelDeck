using System.Globalization;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Presentation;

/// <summary>Names and endpoints describe the event; current resource metadata only annotates its source.</summary>
public sealed class RecentActivityItem : ObservableModel
{
    private string _time = string.Empty, _exactTime = string.Empty;
    private readonly string _serverName;
    private readonly IReadOnlyList<string> _noteKeys;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private TimeZoneInfo _zone = TimeZoneInfo.Local;
    public ActivityEntry Entry { get; }
    public Guid Id => Entry.Id;
    public string AutomationId => $"RecentActivity_{Id:N}";
    public string Name { get; }
    public string Route => $"{Outcome} · {_serverName} · {CompactRoute}";
    public string Outcome => UiText.Outcome(Entry.Outcome);
    public string OutcomeKey => Entry.Outcome.ToString();
    public string CompactRoute => ResourceDisplay.PortRoute(Entry.Requested, Entry.Actual);
    public override string ToString() => Details;
    public string SourceNote => string.Join(" · ", _noteKeys.Select(key => UiText.Get(key)));
    public string Error => UiText.Translate(Entry.Error?.Message);
    public string Time { get => _time; private set => SetProperty(ref _time, value); }
    public string ExactTime { get => _exactTime; private set => SetProperty(ref _exactTime, value); }
    public string Details => UiText.Get("RecentActivityItem_Requested", Name, Route, ResourceDisplay.FullRoute(Entry.Requested)) +
        (Entry.Actual is null ? string.Empty : UiText.Get("RecentActivityItem_Actual", ResourceDisplay.FullRoute(Entry.Requested, Entry.Actual))) +
        $"\n{ExactTime}" + (SourceNote.Length == 0 ? string.Empty : $"\n{SourceNote}") +
        (Error.Length == 0 ? string.Empty : $"\n{Error}");

    public RecentActivityItem(ActivityEntry entry, AppState state)
    {
        Entry = entry;
        RemoteServer? server = state.Servers.FirstOrDefault(item => item.Id == entry.Requested.ServerId);
        ForwardProfile? profile = state.Profiles.FirstOrDefault(item => item.Id == entry.Source.ProfileId);
        string serverName = entry.Source.ServerName;
        string? profileName = entry.Source.ProfileName;
        bool duplicateServer = state.RecentActivity.Any(item => item.Requested.ServerId != entry.Requested.ServerId &&
            string.Equals(item.Source.ServerName, serverName, StringComparison.OrdinalIgnoreCase));
        bool duplicateProfile = profileName is not null && state.RecentActivity.Any(item => item.Source.ProfileId != entry.Source.ProfileId &&
            string.Equals(item.Source.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));
        if (duplicateServer) { serverName += " · " + entry.Requested.ServerId.ToString("N")[..8]; }
        if (duplicateProfile && entry.Source.ProfileId is Guid profileId) { profileName += " · " + profileId.ToString("N")[..8]; }
        Name = profileName ?? serverName;
        _serverName = serverName;
        List<string> notes = [];
        if (server is null) { notes.Add("RecentActivityItem_ServerDeleted"); }
        else if (ResourceDisplay.ServerName(server) != entry.Source.ServerName) { notes.Add("RecentActivityItem_ServerRenamedSinceThisEvent"); }
        if (entry.Source.ProfileId is not null)
        {
            if (profile is null) { notes.Add("RecentActivityItem_ProfileDeleted"); }
            else if (profile.Name != entry.Source.ProfileName) { notes.Add("RecentActivityItem_ProfileRenamedSinceThisEvent"); }
        }
        _noteKeys = notes;
    }

    public void UpdateTime(DateTimeOffset now, TimeZoneInfo zone, CultureInfo culture)
    {
        _now = now;
        _zone = zone;
        ActivityTimeText formatted = ActivityTimeFormatter.Format(Entry.OccurredAt, now, zone, culture);
        Time = formatted.Relative;
        ExactTime = formatted.Exact;
        OnPropertyChanged(nameof(Details));
    }

    public override void RefreshLanguage() { UpdateTime(_now, _zone, UiText.Culture); base.RefreshLanguage(); }
}

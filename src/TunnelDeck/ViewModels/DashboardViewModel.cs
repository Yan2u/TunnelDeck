using System.Globalization;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private readonly TimeProvider _clock = TimeProvider.System;
    private AppState? _state;
    private IReadOnlyList<RecentActivityItem> _allRecent = [], _recent = [];
    private bool _showAll;
    private string _totalForwards = "0";
    private UsageLeader _mostUsedProfile = UsageLeader.Empty, _mostUsedServer = UsageLeader.Empty;
    public IReadOnlyList<RecentActivityItem> Recent { get => _recent; private set => SetProperty(ref _recent, value); }
    public string TotalForwards { get => _totalForwards; private set => SetProperty(ref _totalForwards, value); }
    public UsageLeader MostUsedProfile { get => _mostUsedProfile; private set => SetProperty(ref _mostUsedProfile, value); }
    public UsageLeader MostUsedServer { get => _mostUsedServer; private set => SetProperty(ref _mostUsedServer, value); }
    public bool CanExpandRecent => _allRecent.Count > 5;
    public string RecentToggleLabel => _showAll ? UiText.Get("DashboardViewModel_ShowLess") : UiText.Get("DashboardViewModel_ShowAllRecentActivity");
    public string RecentSummary => _allRecent.Count == 0 ? string.Empty : UiText.Get("DashboardViewModel_ShowingOfRecentEvents", Recent.Count, _allRecent.Count);

    public void Update(AppState state)
    {
        if (ReferenceEquals(_state, state)) { return; }
        _state = state;
        _allRecent = state.RecentActivity.OrderByDescending(entry => entry.OccurredAt)
            .Select(entry => new RecentActivityItem(entry, state)).ToArray();
        RefreshTimes();
        UpdateVisibleRecent();
        UsageStatisticsProjection statistics = UsageStatisticsProjection.Create(state, UiText.Culture);
        TotalForwards = statistics.TotalForwards;
        MostUsedProfile = statistics.Profile;
        MostUsedServer = statistics.Server;
    }

    public void RefreshTimes()
    {
        DateTimeOffset now = _clock.GetUtcNow();
        foreach (RecentActivityItem item in _allRecent) { item.UpdateTime(now, TimeZoneInfo.Local, UiText.Culture); }
    }

    public override void RefreshLanguage()
    {
        RefreshTimes();
        if (_state is not null)
        {
            UsageStatisticsProjection statistics = UsageStatisticsProjection.Create(_state, UiText.Culture);
            TotalForwards = statistics.TotalForwards;
            MostUsedProfile = statistics.Profile;
            MostUsedServer = statistics.Server;
        }
        base.RefreshLanguage();
    }

    public void ToggleRecent() { _showAll = !_showAll; UpdateVisibleRecent(); }

    private void UpdateVisibleRecent()
    {
        Recent = _showAll ? _allRecent : _allRecent.Take(5).ToArray();
        OnPropertyChanged(nameof(CanExpandRecent));
        OnPropertyChanged(nameof(RecentToggleLabel));
        OnPropertyChanged(nameof(RecentSummary));
    }
}

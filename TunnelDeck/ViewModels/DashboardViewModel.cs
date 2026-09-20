using System.Globalization;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;

namespace TunnelDeck.ViewModels;

public sealed class DashboardViewModel : ObservableObject
{
    private IReadOnlyList<RecentRow> _recent = [];
    private string _totalForwards = "0";
    private string _mostUsedProfile = "—";
    private string _mostUsedServer = "—";
    public IReadOnlyList<RecentRow> Recent { get => _recent; private set => SetProperty(ref _recent, value); }
    public string TotalForwards { get => _totalForwards; private set => SetProperty(ref _totalForwards, value); }
    public string MostUsedProfile { get => _mostUsedProfile; private set => SetProperty(ref _mostUsedProfile, value); }
    public string MostUsedServer { get => _mostUsedServer; private set => SetProperty(ref _mostUsedServer, value); }

    public void Update(AppState state)
    {
        Recent = state.RecentActivity.OrderByDescending(entry => entry.OccurredAt).Take(5).Select(entry => new RecentRow(
            entry.Source.ProfileName ?? entry.Source.ServerName,
            entry.Outcome + " · " + entry.Source.ServerName + " · " + ResourceDisplay.PortRoute(entry.Requested, entry.Actual),
            entry.OccurredAt.ToLocalTime().ToString("t", CultureInfo.CurrentCulture), entry.Outcome.ToString())).ToArray();
        TotalForwards = state.Statistics.TotalForwards.ToString("N0", CultureInfo.CurrentCulture);
        Guid topProfile = state.Statistics.ProfileUses.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).FirstOrDefault().Key;
        Guid topServer = state.Statistics.ServerForwards.OrderByDescending(entry => entry.Value).ThenBy(entry => entry.Key).FirstOrDefault().Key;
        MostUsedProfile = state.Profiles.FirstOrDefault(profile => profile.Id == topProfile)?.Name ?? "—";
        RemoteServer? server = state.Servers.FirstOrDefault(item => item.Id == topServer);
        MostUsedServer = server is null ? "—" : ResourceDisplay.ServerName(server);
    }
}

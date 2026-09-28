using System.Globalization;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Presentation;

public sealed record UsageLeader(Guid? Id, string Name, string Detail, long Count)
{
    public static UsageLeader Empty => CreateEmpty(UiText.Culture);
    public static UsageLeader CreateEmpty(CultureInfo culture) =>
        new(null, UiText.Format("UsageStatisticsProjection_NoUsageYet", culture), string.Empty, 0);
}

/// <summary>Lifetime totals include deleted resources; rankings intentionally consider only current resources.</summary>
public sealed record UsageStatisticsProjection(string TotalForwards, UsageLeader Server, UsageLeader Profile)
{
    public static UsageStatisticsProjection Create(AppState state, CultureInfo culture)
    {
        RemoteServer? server = state.Servers.Where(item => state.Statistics.ServerForwards.GetValueOrDefault(item.Id) > 0)
            .OrderByDescending(item => state.Statistics.ServerForwards[item.Id]).ThenBy(item => item.Id).FirstOrDefault();
        ForwardProfile? profile = state.Profiles.Where(item => state.Statistics.ProfileUses.GetValueOrDefault(item.Id) > 0)
            .OrderByDescending(item => state.Statistics.ProfileUses[item.Id]).ThenBy(item => item.Id).FirstOrDefault();
        UsageLeader serverLeader = UsageLeader.CreateEmpty(culture), profileLeader = UsageLeader.CreateEmpty(culture);
        if (server is not null)
        {
            string name = ResourceDisplay.ServerName(server);
            bool duplicate = state.Servers.Count(item => string.Equals(ResourceDisplay.ServerName(item), name, StringComparison.OrdinalIgnoreCase)) > 1;
            long count = state.Statistics.ServerForwards[server.Id];
            serverLeader = new(server.Id, duplicate ? name + " · " + server.Id.ToString("N")[..8] : name,
                UiText.Format(count == 1 ? "Usage_ForwardsOne" : "Usage_ForwardsOther", culture, count.ToString("N0", culture), ResourceDisplay.ServerEndpoint(server)), count);
        }
        if (profile is not null)
        {
            bool duplicate = state.Profiles.Count(item => string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase)) > 1;
            long count = state.Statistics.ProfileUses[profile.Id];
            profileLeader = new(profile.Id, duplicate ? profile.Name + " · " + profile.Id.ToString("N")[..8] : profile.Name,
                UiText.Format(count == 1 ? "Usage_UsesOne" : "Usage_UsesOther", culture, count.ToString("N0", culture)), count);
        }
        return new(state.Statistics.TotalForwards.ToString("N0", culture), serverLeader, profileLeader);
    }
}

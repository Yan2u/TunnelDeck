using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Navigation;

public enum NavigationDestination { Dashboard, Profiles, ProfileDetail, Servers, ServerDetail, Settings, Import, ExportServers, ExportProfiles }

public sealed record AppRoute
{
    public NavigationDestination Destination { get; }
    public Guid? ResourceId { get; }

    public AppRoute(NavigationDestination destination, Guid? resourceId = null)
    {
        if (!Enum.IsDefined(destination)) { throw new ArgumentOutOfRangeException(nameof(destination)); }
        if (resourceId == Guid.Empty || resourceId.HasValue && destination is not (NavigationDestination.ProfileDetail or NavigationDestination.ServerDetail))
        {
            throw new ArgumentException(UiText.Invariant("Diagnostic_AppRoute_OnlyResourceDetailsCanCarryANonemptyResource"), nameof(resourceId));
        }
        Destination = destination;
        ResourceId = resourceId;
    }

    public static AppRoute Dashboard { get; } = new(NavigationDestination.Dashboard);
    public static AppRoute Profiles { get; } = new(NavigationDestination.Profiles);
    public static AppRoute Servers { get; } = new(NavigationDestination.Servers);
    public static AppRoute Settings { get; } = new(NavigationDestination.Settings);
    public static AppRoute Import { get; } = new(NavigationDestination.Import);
    public static AppRoute ExportServers { get; } = new(NavigationDestination.ExportServers);
    public static AppRoute ExportProfiles { get; } = new(NavigationDestination.ExportProfiles);
    public static AppRoute Profile(Guid? id = null) => new(NavigationDestination.ProfileDetail, id);
    public static AppRoute Server(Guid? id = null) => new(NavigationDestination.ServerDetail, id);

    public bool ExistsIn(AppState state) => ResourceId is not Guid id || Destination switch
    {
        NavigationDestination.ProfileDetail => state.Profiles.Any(profile => profile.Id == id),
        NavigationDestination.ServerDetail => state.Servers.Any(server => server.Id == id),
        _ => true,
    };

    public AppRoute Parent => Destination switch
    {
        NavigationDestination.ProfileDetail => Profiles,
        NavigationDestination.ServerDetail => Servers,
        NavigationDestination.Import or NavigationDestination.ExportServers => Servers,
        NavigationDestination.ExportProfiles => Profiles,
        _ => Dashboard,
    };
}

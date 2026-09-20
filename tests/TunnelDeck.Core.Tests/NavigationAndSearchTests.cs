using TunnelDeck.Core.Models;
using TunnelDeck.Core.Navigation;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Tests.Fixtures;

namespace TunnelDeck.Core.Tests;

public sealed class NavigationAndSearchTests
{
    [Fact]
    public void FinishingASaveCannotStealNavigationFromANewerUserChoice()
    {
        var history = new NavigationHistory();
        AppRoute editing = AppRoute.Server();
        history.Navigate(editing);
        history.Navigate(AppRoute.Settings);
        Assert.False(history.ReplaceIfCurrent(editing, AppRoute.Server(DomainData.Id(1))));
        Assert.Equal(AppRoute.Settings, history.Current);
        history.GoBack();
        Assert.True(history.ReplaceIfCurrent(editing, AppRoute.Server(DomainData.Id(1))));
        Assert.Single(history.BackStack);
    }

    [Fact]
    public void RepeatedSidebarOrSearchSelectionDoesNotPushTheSamePage()
    {
        var history = new NavigationHistory();
        Assert.False(history.Navigate(AppRoute.Dashboard));
        Assert.True(history.Navigate(AppRoute.Servers));
        Assert.True(history.Navigate(AppRoute.Server(DomainData.Id(1))));
        Assert.False(history.Navigate(AppRoute.Server(DomainData.Id(1))));
        Assert.Equal(2, history.BackStack.Length);
        Assert.True(history.GoBack());
        Assert.Equal(AppRoute.Servers, history.Current);
        Assert.True(history.GoBack());
        Assert.Equal(AppRoute.Dashboard, history.Current);
        Assert.False(history.GoBack());
    }

    [Fact]
    public void NewAndExistingDetailsShareADestinationButHaveDistinctRoutes()
    {
        var history = new NavigationHistory();
        history.Navigate(AppRoute.Profiles);
        history.Navigate(AppRoute.Profile());
        history.Replace(AppRoute.Profile(DomainData.Id(10)));
        Assert.Equal(2, history.BackStack.Length);
        history.GoBack();
        Assert.Equal(AppRoute.Profiles, history.Current);
        Assert.Throws<ArgumentException>(() => new AppRoute(NavigationDestination.Settings, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => AppRoute.Server(Guid.Empty));
    }

    [Fact]
    public void MissingResourcesAreRemovedFromHistoryAndCurrentDetailFallsBackToItsList()
    {
        var history = new NavigationHistory();
        history.Navigate(AppRoute.Servers);
        history.Navigate(AppRoute.Server(DomainData.Id(1)));
        history.Navigate(AppRoute.Server(DomainData.Id(2)));
        Assert.True(history.Reconcile(new AppState()));
        Assert.Equal(AppRoute.Servers, history.Current);
        Assert.Equal([AppRoute.Dashboard], history.BackStack.ToArray());
        Assert.False(history.Reconcile(new AppState()));
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("WEB", 2)]
    [InlineData("server-2", 1)]
    [InlineData("root", 1)]
    [InlineData("staging root", 1)]
    [InlineData("missing", 0)]
    [InlineData("2001:db8", 1)]
    public void SearchCoversNamesHostsUsersAndWhitespaceSeparatedWords(string query, int expected)
    {
        Assert.Equal(expected, ResourceSearch.Find(State(), query).Length);
    }

    [Fact]
    public void ExactNamesPrecedePrefixesThenSubstringAndOrderingIsStable()
    {
        AppState state = DomainData.State([], [
            DomainData.Profile(1) with { Name = "My Web" },
            DomainData.Profile(2) with { Name = "Web tools" },
            DomainData.Profile(3) with { Name = "Web" },
        ]);
        Assert.Equal(["Web", "Web tools", "My Web"], ResourceSearch.Find(state, "web").Select(item => item.Name).ToArray());
        Assert.Equal(2, ResourceSearch.Find(state, "web", 2).Length);
        Assert.Throws<ArgumentOutOfRangeException>(() => ResourceSearch.Find(state, "web", 0));
    }

    [Fact]
    public void DuplicateNamesStayDistinctAndCarryUsefulDescriptionsAndIds()
    {
        var first = DomainData.Server(1, "Same");
        var second = DomainData.Server(2, "Same");
        var state = DomainData.State([second, first], [DomainData.Profile(1) with { Name = "Same" }, DomainData.Profile(2) with { Name = "Same" }]);
        var results = ResourceSearch.Find(state, "Same");
        Assert.Equal(4, results.Length);
        Assert.Equal(4, results.Select(item => item.Route).Distinct().Count());
        Assert.Equal(4, results.Select(item => item.Description).Distinct().Count());
        Assert.Equal(results.ToArray(), ResourceSearch.Find(state with { Servers = [first, second] }, "Same").ToArray());
    }

    [Fact]
    public void SearchDoesNotUseCredentialsOrPrivateKeyPathsAndDoesNotMutateOrdering()
    {
        AppState state = State();
        var order = state.Order;
        Assert.Empty(ResourceSearch.Find(state, "private-file"));
        Assert.Same(order, state.Order);
        Assert.Equal("root@[2001:db8::2]:22", ResourceDisplay.ServerEndpoint(state.Servers[1]));
        Assert.Equal("Remote :5432 → Local :3000", ResourceDisplay.PortRoute(DomainData.Rule(mode: ForwardMode.Remote)));
    }

    private static AppState State() => DomainData.State([
        DomainData.Server(1, "Web development") with { IdentityFilePath = "C:\\private-file" },
        DomainData.Server(2, "Staging") with { Host = "2001:db8::2", Username = "root" },
        DomainData.Server(3) with { Host = "server-2.example" },
    ], [DomainData.Profile() with { Name = "Web tools" }]);
}

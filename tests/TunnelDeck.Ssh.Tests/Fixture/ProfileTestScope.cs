using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Profiles;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Ssh.Tests.Fixture;

internal sealed class ProfileTestScope : IAsyncDisposable
{
    public TunnelTestScope Backend { get; }
    public TunnelActivityRecorder Activity { get; }
    public ProfileRunner Runner { get; }
    public ForwardProfile Profile { get; private set; } = new() { Id = Guid.NewGuid(), Name = "Disposable cross-server profile" };
    private ProfileTestScope(TunnelTestScope backend)
    {
        Backend = backend;
        Activity = new(backend.Tunnels, backend.Connections.Configuration);
        Runner = new(backend.Connections.Configuration, backend.Tunnels, Activity, (_, _, _) => throw new InvalidOperationException("Saved fixture credentials should be available."));
    }
    public static async Task<ProfileTestScope> CreateAsync(FixtureManifest fixture) => new(await TunnelTestScope.CreateAsync(fixture));
    public async Task<RemoteServer> AddServerAsync(FixtureManifest fixture, int port)
    {
        RemoteServer server = ConnectionTestScope.Server(fixture, port);
        await Backend.Connections.Configuration.SaveServerAsync(server, CredentialChange.Remember(fixture.Password));
        return Backend.Connections.Configuration.Current!.Servers.Single(item => item.Id == server.Id);
    }
    public async Task SaveRulesAsync(params ForwardRule[] rules)
    {
        Profile = Profile with { Rules = [.. rules] };
        await Backend.Connections.Configuration.SaveProfileAsync(Profile, null);
    }
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await Runner.ShutdownAsync(timeout.Token);
        await Backend.Tunnels.ShutdownAsync(timeout.Token);
        await Activity.ShutdownAsync(timeout.Token);
        Runner.Dispose(); await Backend.DisposeAsync();
    }
}

using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Snapshots;
using TunnelDeck.Core.Tunnels;
using TunnelDeck.Infrastructure.Ssh;

namespace TunnelDeck.Ssh.Tests.Fixture;

internal sealed class TunnelTestScope : IAsyncDisposable
{
    public ConnectionTestScope Connections { get; }
    public TunnelManager Tunnels { get; }
    public RemoteServer Server { get; private set; } = null!;

    private TunnelTestScope(SshConnectionOptions? connectionOptions, TimeSpan? attemptTimeout, TunnelManagerOptions? options)
    {
        Connections = new(connectionOptions);
        Tunnels = new(Connections.Manager, new PortSearchPolicy(attemptTimeout: attemptTimeout), options: options);
    }

    public static async Task<TunnelTestScope> CreateAsync(FixtureManifest fixture, int? port = null,
        SshConnectionOptions? connectionOptions = null, TimeSpan? attemptTimeout = null, TunnelManagerOptions? options = null)
    {
        var scope = new TunnelTestScope(connectionOptions, attemptTimeout, options);
        RemoteServer server = ConnectionTestScope.Server(fixture, port);
        await scope.Connections.SaveAsync(server, fixture.Password);
        scope.Server = scope.Connections.Configuration.Current!.Servers.Single();
        return scope;
    }

    public RuleLaunchSnapshot Rule(int localPort, int remotePort, ForwardMode mode = ForwardMode.Local, bool auto = true, RemoteServer? server = null)
    {
        RemoteServer identity = server ?? Server;
        var rule = new ForwardRule
        {
            Id = Guid.NewGuid(), ServerId = identity.Id, Mode = mode,
            LocalPort = localPort, RemotePort = remotePort, AutoFindAvailablePort = mode == ForwardMode.Local && auto,
        };
        return new(identity with { }, rule, new() { ServerName = identity.DisplayName!, RuleId = rule.Id });
    }

    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await Tunnels.ShutdownAsync(deadline.Token);
        await Connections.DisposeAsync();
    }
}

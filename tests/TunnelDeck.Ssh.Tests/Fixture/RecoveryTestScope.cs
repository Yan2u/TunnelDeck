using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tunnels;
using TunnelDeck.Infrastructure.Ssh;

namespace TunnelDeck.Ssh.Tests.Fixture;

internal sealed class RecoveryTestScope : IAsyncDisposable
{
    public TunnelTestScope Backend { get; }
    public TunnelManager Tunnels => Backend.Tunnels;
    public ConnectionTestScope Connections => Backend.Connections;
    public ReconnectCoordinator Reconnect { get; }
    public TunnelActivityRecorder Activity { get; }
    public ResourceDeletionService Deletion { get; }
    public ShutdownCoordinator Shutdown { get; }

    private RecoveryTestScope(TunnelTestScope backend, ReconnectOptions? options)
    {
        Backend = backend;
        Reconnect = new(backend.Tunnels, options ?? new()
        {
            InitialDelay = TimeSpan.FromMilliseconds(150), MaximumDelay = TimeSpan.FromMilliseconds(400),
            RecoveryWindow = TimeSpan.FromSeconds(12), JitterFraction = 0,
        });
        Activity = new(Tunnels, Connections.Configuration);
        Deletion = new(Connections.Configuration, Connections.Manager, Tunnels, Activity);
        Shutdown = new(new ShutdownStep[]
        {
            new("Cancelling work", Connections.Manager.Work), new("Finishing deletion", Deletion),
            new("Stopping recovery", Reconnect), new("Stopping forwards", Tunnels),
            new("Closing connections", Connections.Manager), new("Saving activity", Activity, true),
            new("Saving configuration", Connections.Configuration, true),
        });
    }

    public static async Task<RecoveryTestScope> CreateAsync(FixtureManifest fixture, int? port = null,
        ReconnectOptions? reconnect = null, SshConnectionOptions? connections = null, TunnelManagerOptions? tunnels = null,
        TimeSpan? attemptTimeout = null) => new(await TunnelTestScope.CreateAsync(fixture, port,
            connections ?? new() { ConnectTimeout = TimeSpan.FromSeconds(2), LibraryTimeout = TimeSpan.FromSeconds(3), ControlTimeout = TimeSpan.FromSeconds(1) },
            attemptTimeout, tunnels), reconnect);

    public ActiveTunnel Tunnel(Guid id) => Tunnels.Current.Tunnels.Single(tunnel => tunnel.Id == id);
    public async Task WaitAsync(Func<bool> condition)
    {
        try { await UntilAsync(condition); }
        catch (OperationCanceledException)
        {
            string tunnels = string.Join("; ", Tunnels.Current.Tunnels.Select(tunnel =>
                $"{tunnel.Requested.Mode}: {tunnel.State}, attempt={tunnel.ReconnectAttempt}, recovered={tunnel.ReconnectCount}, next={tunnel.NextReconnectAt:O}, stop={tunnel.StopRequested}, failure={tunnel.LastError?.Kind}"));
            string sessions = string.Join("; ", Connections.Factory.Sessions.Select(session =>
                $"{session.Mode}: disconnected={session.Disconnected.IsCancellationRequested}, disposed={session.IsDisposed}"));
            throw new Xunit.Sdk.XunitException($"Recovery did not reach the expected state. Groups={Reconnect.ActiveServerCount}; tracked={Connections.Manager.TrackedConnectionCount}. Tunnels: {tunnels}. Sessions: {sessions}");
        }
    }
    public static async Task UntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        using var deadline = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(15));
        while (!condition()) { await Task.Delay(20, deadline.Token); }
    }

    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await Shutdown.ShutdownAsync(deadline.Token);
        await Backend.DisposeAsync();
    }
}

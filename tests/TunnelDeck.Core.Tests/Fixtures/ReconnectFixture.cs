using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Lifecycle;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Tests.Fixtures;

internal sealed class ReconnectFixture : IAsyncDisposable
{
    public TimerTimeProvider Clock { get; } = new();
    public TunnelFixture Backend { get; private set; } = null!;
    public ReconnectCoordinator Reconnect { get; private set; } = null!;
    public TunnelActivityRecorder Activity { get; private set; } = null!;
    public ResourceDeletionService Deletion { get; private set; } = null!;
    public static async Task<ReconnectFixture> CreateAsync(TimeSpan? recoveryWindow = null)
    {
        var fixture = new ReconnectFixture();
        RemoteServer[] servers = [DomainData.Server() with { CredentialReference = DomainData.Id(900) },
            DomainData.Server(2) with { CredentialReference = DomainData.Id(901) }];
        fixture.Backend = await TunnelFixture.CreateAsync(clock: fixture.Clock, servers: servers);
        foreach (var server in servers) { fixture.Backend.Connections.Credentials.Write(server.CredentialReference!.Value, "disposable-reconnect-input"); }
        fixture.Activity = new(fixture.Backend.Manager, fixture.Backend.Connections.Configuration);
        fixture.Reconnect = new(fixture.Backend.Manager,
            new() { JitterFraction = 0, RecoveryWindow = recoveryWindow ?? TimeSpan.FromMinutes(2) }, fixture.Clock);
        fixture.Deletion = new(fixture.Backend.Connections.Configuration, fixture.Backend.Connections.Manager, fixture.Backend.Manager, fixture.Activity);
        return fixture;
    }

    public ActiveTunnel Tunnel(Guid id) => Backend.Manager.Current.Tunnels.Single(tunnel => tunnel.Id == id);
    public async Task NextRoundAsync()
    {
        // Scheduling is published just before Task.Delay installs its timer. Do not advance
        // the recovery budget timer while the backoff timer is still being created.
        await UntilAsync(() => Backend.Manager.Current.Tunnels.Any(tunnel => tunnel.NextReconnectAt is { } next &&
            Clock.NextDue is { } due && due == next - Clock.GetUtcNow()));
        Clock.Advance(Clock.NextDue!.Value);
    }
    public static async Task UntilAsync(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition()) { await Task.Delay(5, deadline.Token); }
    }
    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Deletion.ShutdownAsync(deadline.Token);
        await Reconnect.ShutdownAsync(deadline.Token);
        await Backend.Manager.ShutdownAsync(deadline.Token);
        await Activity.ShutdownAsync(deadline.Token);
        await Backend.DisposeAsync();
    }
}

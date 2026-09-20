using System.Collections.Concurrent;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Profiles;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Tests.Fixtures;

internal sealed class ProfileFixture : IAsyncDisposable
{
    public TunnelFixture Backend { get; }
    public TunnelActivityRecorder Activity { get; }
    public ProfileRunner Runner { get; }
    public ForwardProfile Profile { get; }
    public ConcurrentQueue<Guid> Prompts { get; } = new();
    public Func<RemoteServer, CancellationToken, Task<ConnectionCredentials?>> Prompt { get; set; } = (_, _) => Task.FromResult<ConnectionCredentials?>(ConnectionFixture.Input);
    private ProfileFixture(TunnelFixture backend, ForwardProfile profile, bool networkAllowed)
    {
        Backend = backend; Profile = profile;
        Activity = new(backend.Manager, backend.Connections.Configuration);
        Runner = new(backend.Connections.Configuration, backend.Manager, Activity,
            (server, _, token) => { Prompts.Enqueue(server.Id); return Prompt(server, token); }, networkAllowed);
    }
    public static async Task<ProfileFixture> CreateAsync(bool networkAllowed = true, params ForwardRule[] rules)
    {
        var backend = await TunnelFixture.CreateAsync();
        ForwardProfile profile = DomainData.Profile(1, rules);
        await backend.Connections.Configuration.SaveProfileAsync(profile, null);
        return new(backend, profile, networkAllowed);
    }
    public async ValueTask DisposeAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        Backend.Connections.Store.BeforeCommit = null;
        await Runner.ShutdownAsync(timeout.Token);
        await Backend.Manager.ShutdownAsync(timeout.Token);
        await Activity.ShutdownAsync(timeout.Token);
        Runner.Dispose(); await Backend.DisposeAsync();
    }
}

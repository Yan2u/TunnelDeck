using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Deletion;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Profiles;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Tests.Fixtures;

internal sealed class DeletionFixture : IAsyncDisposable
{
    public TunnelFixture Backend { get; }
    public ConfigurationService Configuration => Backend.Connections.Configuration;
    public TunnelActivityRecorder Activity { get; }
    public ProfileRunner Profiles { get; }
    public TunnelWorkspace Workspace { get; }
    public QueuedViewDispatcher Dispatcher { get; } = new();
    public ResourceDeletionService Deletion { get; }
    public Func<RemoteServer, CancellationToken, Task<ConnectionCredentials?>> Prompt { get; set; } =
        (_, _) => Task.FromResult<ConnectionCredentials?>(ConnectionFixture.Input);

    private DeletionFixture(TunnelFixture backend)
    {
        Backend = backend;
        Activity = new(backend.Manager, Configuration);
        Profiles = new(Configuration, backend.Manager, Activity, (server, _, token) => Prompt(server, token));
        Workspace = new(backend.Manager, Activity, Configuration, Dispatcher, (server, _, token) => Prompt(server, token));
        Deletion = new(Configuration, backend.Connections.Manager, backend.Manager, Activity);
    }

    public static async Task<DeletionFixture> CreateAsync(params ForwardRule[] rules)
    {
        var backend = await TunnelFixture.CreateAsync();
        await backend.Connections.Configuration.SaveProfileAsync(DomainData.Profile(1, rules), null);
        return new(backend);
    }

    public async ValueTask DisposeAsync()
    {
        Backend.Connections.Store.BeforeCommit = null;
        foreach (var handle in Backend.Handles) { handle.OnStop = null; handle.OnDispose = null; }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Dispatcher.RunAsync(Deletion.ShutdownAsync(timeout.Token));
        await Dispatcher.RunAsync(Profiles.ShutdownAsync(timeout.Token));
        await Dispatcher.RunAsync(Workspace.ShutdownAsync(timeout.Token));
        await Dispatcher.RunAsync(Backend.Manager.ShutdownAsync(timeout.Token));
        await Activity.ShutdownAsync(timeout.Token);
        Profiles.Dispose(); Workspace.Dispose();
        await Backend.DisposeAsync();
    }
}

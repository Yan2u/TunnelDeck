using System.Collections.Concurrent;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Tests.Fixtures;

internal sealed class WorkspaceFixture : IAsyncDisposable
{
    public TunnelFixture Backend { get; }
    public QueuedViewDispatcher Dispatcher { get; } = new();
    public TunnelActivityRecorder Activity { get; }
    public TunnelWorkspace Workspace { get; }
    public int PromptCount { get; private set; }
    public Func<CancellationToken, Task<ConnectionCredentials?>> Prompt { get; set; } = _ => Task.FromResult<ConnectionCredentials?>(ConnectionFixture.Input);
    private WorkspaceFixture(TunnelFixture backend, bool preview)
    {
        Backend = backend;
        Activity = new(backend.Manager, backend.Connections.Configuration);
        Workspace = new(backend.Manager, Activity, backend.Connections.Configuration, Dispatcher,
            (_, _, token) => { PromptCount++; return Prompt(token); }, preview);
        Workspace.Quick.SelectedServerId = DomainData.Id(1);
        Workspace.Quick.PortText = "3000";
    }
    public static async Task<WorkspaceFixture> CreateAsync(bool remember = true, bool preview = false)
    {
        Guid reference = DomainData.Id(90000);
        var backend = await TunnelFixture.CreateAsync(servers: [DomainData.Server() with { CredentialReference = remember ? reference : null }, DomainData.Server(2)]);
        if (remember) { backend.Connections.Credentials.Values[reference] = ConnectionFixture.Input.Secret!; }
        return new(backend, preview);
    }
    public Task RunAsync(Task work) => Dispatcher.RunAsync(work);
    public async ValueTask DisposeAsync()
    {
        await RunAsync(Workspace.ShutdownAsync(CancellationToken.None));
        await RunAsync(Backend.Manager.ShutdownAsync(CancellationToken.None));
        await RunAsync(Activity.ShutdownAsync(CancellationToken.None));
        Workspace.Dispose();
        await Backend.DisposeAsync();
    }
}

internal sealed class QueuedViewDispatcher : IViewDispatcher
{
    private readonly ConcurrentQueue<Action> _pending = new();
    private int _dispatchThreadId;
    public bool HasThreadAccess => Volatile.Read(ref _dispatchThreadId) == Environment.CurrentManagedThreadId;
    public bool TryEnqueue(Action action) { _pending.Enqueue(action); return true; }
    public void Drain(bool reverse = false)
    {
        var actions = new List<Action>();
        while (_pending.TryDequeue(out var action)) { actions.Add(action); }
        if (reverse) { actions.Reverse(); }
        Volatile.Write(ref _dispatchThreadId, Environment.CurrentManagedThreadId);
        try { foreach (Action action in actions) { action(); } }
        finally { Volatile.Write(ref _dispatchThreadId, 0); }
    }
    public async Task UntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        while (!condition()) { Drain(); await Task.Delay(1, timeout.Token); }
        Drain();
    }
    public async Task RunAsync(Task task) { await UntilAsync(() => task.IsCompleted); await task; Drain(); }
}

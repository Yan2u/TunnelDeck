using System.Collections.Concurrent;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Snapshots;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Tests.Fixtures;

internal sealed class TunnelFixture : IAsyncDisposable
{
    public ConnectionFixture Connections { get; }
    public TunnelManager Manager { get; }
    public ConcurrentQueue<ForwardRule> Attempts { get; } = new();
    public ConcurrentBag<TestForward> Handles { get; } = [];
    public Func<ConnectionFixture.TestSession, ForwardRule, CancellationToken, Task<IForwardHandle>>? Start { get; set; }

    private TunnelFixture(ConnectionFixture connections, TimeProvider? clock, TunnelManagerOptions? options, PortSearchPolicy? ports)
    {
        Connections = connections;
        Manager = new(connections.Manager, ports, clock, options);
        Connections.Factory.StartForward = (session, rule, token) =>
        {
            Attempts.Enqueue(rule);
            return Start?.Invoke(session, rule, token) ?? Task.FromResult<IForwardHandle>(CreateHandle(session, rule));
        };
    }

    public static async Task<TunnelFixture> CreateAsync(TimeProvider? clock = null, TunnelManagerOptions? options = null,
        PortSearchPolicy? ports = null, params RemoteServer[] servers) => new(await ConnectionFixture.CreateAsync(true, servers), clock, options, ports);

    public RuleLaunchSnapshot Launch(int number = 1, int server = 1, ForwardMode mode = ForwardMode.Local, int port = 3000, bool auto = true)
    {
        RemoteServer saved = Connections.Configuration.Current!.Servers.First(item => item.Id == DomainData.Id(server));
        ForwardRule rule = DomainData.Rule(number, server, mode, port, 5432, auto);
        return new(saved with { }, rule, new() { ServerName = saved.Host, RuleId = rule.Id });
    }

    public TestForward CreateHandle(ConnectionFixture.TestSession session, ForwardRule rule)
    {
        var handle = new TestForward(session, rule.GetRoute());
        Handles.Add(handle);
        return handle;
    }

    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Manager.ShutdownAsync(deadline.Token);
        await Connections.DisposeAsync();
    }

    internal sealed class TestForward(ConnectionFixture.TestSession session, ForwardRoute actual) : IForwardHandle
    {
        private bool _stopped;
        public bool Disposed { get; private set; }
        public ForwardRoute Actual { get; } = actual;
        public bool IsStarted => !_stopped && !Disposed && !session.Disconnected.IsCancellationRequested;
        public int StopCalls { get; private set; }
        public int DisposeCalls { get; private set; }
        public Func<CancellationToken, Task>? OnStop { get; set; }
        public Func<Task>? OnDispose { get; set; }
        public ForwardActivity Activity { get; private set; } = new();
        public event Action<ForwardActivity>? ActivityChanged;
        public void Report(ForwardActivity activity) { Activity = activity with { Revision = Activity.Revision + 1 }; ActivityChanged?.Invoke(Activity); }
        public void Replay(ForwardActivity activity) => ActivityChanged?.Invoke(activity);
        public async Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (!IsStarted) { return; }
            StopCalls++;
            if (OnStop is not null) { await OnStop(cancellationToken); }
            _stopped = true;
        }
        public async ValueTask DisposeAsync()
        {
            if (Disposed) { return; }
            DisposeCalls++;
            if (OnDispose is not null) { await OnDispose(); }
            _stopped = true;
            Disposed = true;
        }
    }
}

internal sealed class AsyncGate : IDisposable
{
    public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WaitAsync(CancellationToken token = default)
    {
        Entered.TrySetResult();
        return Release.Task.WaitAsync(token);
    }
    public void Dispose() => Release.TrySetResult();
}

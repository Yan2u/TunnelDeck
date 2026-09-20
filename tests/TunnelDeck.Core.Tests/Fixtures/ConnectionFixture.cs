using System.Collections.Concurrent;
using System.Collections.Immutable;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Security;

namespace TunnelDeck.Core.Tests.Fixtures;

internal sealed class ConnectionFixture : IAsyncDisposable
{
    public MemoryStateStore Store { get; }
    public TestCredentials Credentials { get; } = new();
    public TestHostKeys HostKeys { get; } = new();
    public TestApproval Approval { get; } = new();
    public TestSessionFactory Factory { get; } = new();
    public ConfigurationService Configuration { get; }
    public SshConnectionManager Manager { get; }
    public static ConnectionCredentials Input => new("disposable-test-input");

    private ConnectionFixture(RemoteServer[] servers, bool networkEnabled)
    {
        Store = new(DomainData.State([.. servers], []));
        Configuration = new(Store, Credentials, new TestCleanupStore(), new TestConfigurationLock());
        Manager = new(Configuration, Factory, Credentials, HostKeys, Approval, networkEnabled);
    }

    public static async Task<ConnectionFixture> CreateAsync(bool networkEnabled = true, params RemoteServer[] servers)
    {
        var fixture = new ConnectionFixture(servers.Length == 0 ? [DomainData.Server(), DomainData.Server(2)] : servers, networkEnabled);
        await fixture.Configuration.InitializeAsync();
        return fixture;
    }

    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Manager.ShutdownAsync(deadline.Token);
        await Configuration.ShutdownAsync(deadline.Token);
    }

    internal sealed class MemoryStateStore(AppState state) : IStateStore
    {
        public AppState State { get; private set; } = state;
        public Func<Task>? BeforeCommit { get; set; }
        public Task<StateLoadResult> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new StateLoadResult(State, []));
        public async Task<AppState> CommitAsync(AppState candidate, long expectedRevision, CancellationToken cancellationToken = default)
        {
            if (BeforeCommit is not null) { await BeforeCommit(); }
            cancellationToken.ThrowIfCancellationRequested();
            if (State.Revision != expectedRevision) { throw new StateRevisionConflictException(expectedRevision, State.Revision); }
            return State = candidate with { Revision = expectedRevision + 1 };
        }
    }

    internal sealed class TestCredentials : ICredentialStore
    {
        public ConcurrentDictionary<Guid, string> Values { get; } = new();
        public string? Read(Guid reference) => Values.GetValueOrDefault(reference);
        public void Write(Guid reference, string secret) => Values[reference] = secret;
        public void Delete(Guid reference) => Values.TryRemove(reference, out _);
    }

    internal sealed class TestHostKeys : IHostKeyStore
    {
        private readonly object _sync = new();
        private readonly Dictionary<(string, int, string), TrustedHostKey> _keys = [];
        public int Count { get { lock (_sync) { return _keys.Count; } } }
        public bool Fail { get; set; }
        public Task<TrustedHostKey?> FindAsync(string host, int port, string algorithm, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail) { throw new IOException("Synthetic trust I/O failure."); }
            lock (_sync) { return Task.FromResult(_keys.GetValueOrDefault((host, port, algorithm))); }
        }
        public Task TrustAsync(TrustedHostKey key, CancellationToken cancellationToken = default)
        {
            lock (_sync) { _keys[(key.Identity.Host, key.Identity.Port, key.Identity.Algorithm)] = key; }
            return Task.CompletedTask;
        }
        public Task TrustNewAsync(TrustedHostKey key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Fail) { throw new IOException("Synthetic trust I/O failure."); }
            lock (_sync)
            {
                var id = (key.Identity.Host, key.Identity.Port, key.Identity.Algorithm);
                if (_keys.TryGetValue(id, out TrustedHostKey? old) && !old.Identity.Matches(key.Identity))
                {
                    throw new ConnectionException(ConnectionFailure.HostKeyChanged);
                }
                _keys[id] = key;
            }
            return Task.CompletedTask;
        }
    }

    internal sealed class TestApproval : IHostKeyApproval
    {
        public ConcurrentQueue<HostKeyApprovalRequest> Requests { get; } = new();
        public Func<HostKeyApprovalRequest, CancellationToken, Task<bool>> Handle { get; set; } = (_, _) => Task.FromResult(true);
        public Task<bool> ConfirmAsync(HostKeyApprovalRequest request, CancellationToken cancellationToken)
        {
            Requests.Enqueue(request);
            return Handle(request, cancellationToken);
        }
    }

    internal sealed class TestSessionFactory : ISshSessionFactory
    {
        public ConcurrentQueue<SshSessionRequest> Requests { get; } = new();
        public ConcurrentBag<TestSession> Sessions { get; } = [];
        public Func<RemoteServer, HostKeyIdentity> KeyFor { get; set; } = server => Key(server);
        public Func<SshSessionRequest, CancellationToken, Task> BeforeConnect { get; set; } = (_, _) => Task.CompletedTask;
        public Func<TestSession, Task>? DisposeSession { get; set; }
        public Func<TestSession, ForwardRule, CancellationToken, Task<IForwardHandle>>? StartForward { get; set; }
        public static HostKeyIdentity Key(RemoteServer server, byte value = 1) => HostKeyIdentity.FromKey(server.Host, server.Port, "ssh-ed25519", [value]);

        public async Task<ISshSession> OpenAsync(SshSessionRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Requests.Enqueue(request);
            HostKeyIdentity key = KeyFor(request.Server);
            if (request.TrustedKey?.Matches(key) != true) { throw new HostKeyNotTrustedException(key); }
            await BeforeConnect(request, cancellationToken);
            // Intentionally allow a late success after cancellation, to exercise manager ownership.
            var session = new TestSession(request.Server.Id, request.Mode, key, DisposeSession, StartForward);
            Sessions.Add(session);
            return session;
        }
    }

    internal sealed class TestSession(Guid serverId, ForwardMode mode, HostKeyIdentity key, Func<TestSession, Task>? dispose,
        Func<TestSession, ForwardRule, CancellationToken, Task<IForwardHandle>>? startForward) : ISshSession
    {
        private readonly CancellationTokenSource _disconnect = new();
        private int _disposeCount;
        public Guid ServerId { get; } = serverId;
        public ForwardMode Mode { get; } = mode;
        public HostKeyIdentity HostKey { get; } = key;
        public CancellationToken Disconnected => _disconnect.Token;
        public int DisposeCount => _disposeCount;
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Disconnect() => _disconnect.Cancel();
        public Task<IForwardHandle> StartAsync(ForwardRule rule, CancellationToken cancellationToken = default) =>
            startForward?.Invoke(this, rule, cancellationToken) ?? throw new NotSupportedException("Test transport has no forwarding implementation.");
        public async ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            if (dispose is not null) { await dispose(this); }
            _disconnect.Cancel();
            Disposed.TrySetResult();
        }
    }

    private sealed class TestCleanupStore : ICredentialCleanupStore
    {
        private ImmutableHashSet<Guid> _references = [];
        public Task<ImmutableHashSet<Guid>> ReadAsync(CancellationToken cancellationToken = default) => Task.FromResult(_references);
        public Task AddAsync(IEnumerable<Guid> references, CancellationToken cancellationToken = default) { _references = _references.Union(references); return Task.CompletedTask; }
        public Task RemoveAsync(Guid reference, CancellationToken cancellationToken = default) { _references = _references.Remove(reference); return Task.CompletedTask; }
    }

    private sealed class TestConfigurationLock : IConfigurationLock
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        public async ValueTask<IDisposable> AcquireAsync(CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken);
            return new Lease(_gate);
        }
        private sealed class Lease(SemaphoreSlim gate) : IDisposable { public void Dispose() => gate.Release(); }
    }
}

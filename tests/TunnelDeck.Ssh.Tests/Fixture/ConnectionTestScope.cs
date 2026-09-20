using System.Collections.Concurrent;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Connections;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Core.Security;
using TunnelDeck.Infrastructure.Persistence;
using TunnelDeck.Infrastructure.Security;
using TunnelDeck.Infrastructure.Ssh;

namespace TunnelDeck.Ssh.Tests.Fixture;

internal sealed class ConnectionTestScope : IAsyncDisposable
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TunnelDeck.Ssh.ConnectionTests"));
    public AppDataDirectory Directory { get; } = new(Path.Combine(Root, Guid.NewGuid().ToString("N")));
    public TestCredentials Credentials { get; } = new();
    public TestApproval Approval { get; } = new();
    public CountingFactory Factory { get; }
    public JsonHostKeyStore HostKeys { get; }
    public ConfigurationService Configuration { get; }
    public SshConnectionManager Manager { get; }

    public ConnectionTestScope(SshConnectionOptions? options = null)
    {
        Factory = new(options);
        Configuration = new(new JsonStateStore(Directory), Credentials, new JsonCredentialCleanupStore(Directory), new FileConfigurationLock(Directory.TransactionLockPath));
        HostKeys = new(Directory);
        Manager = new(Configuration, Factory, Credentials, HostKeys, Approval);
    }

    public async Task SaveAsync(RemoteServer server, string? secret = null)
    {
        await Configuration.InitializeAsync();
        await Configuration.SaveServerAsync(server, secret is null ? CredentialChange.Forget : CredentialChange.Remember(secret));
    }

    public static RemoteServer Server(FixtureManifest manifest, int? port = null) => new()
    {
        Id = Guid.NewGuid(), DisplayName = "Disposable OpenSSH", Host = manifest.Host,
        Port = port ?? manifest.AllowPort, Username = manifest.Username,
    };

    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Manager.ShutdownAsync(deadline.Token);
        await Configuration.ShutdownAsync(deadline.Token);
        string target = Path.GetFullPath(Directory.RootPath);
        if (!target.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(target), "N", out _)) { throw new InvalidOperationException("Unsafe test cleanup path."); }
        if (System.IO.Directory.Exists(target)) { System.IO.Directory.Delete(target, true); }
    }

    internal sealed class TestCredentials : ICredentialStore
    {
        private readonly ConcurrentDictionary<Guid, string> _values = new();
        public string? Read(Guid reference) => _values.GetValueOrDefault(reference);
        public void Write(Guid reference, string secret) => _values[reference] = secret;
        public void Delete(Guid reference) => _values.TryRemove(reference, out _);
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

    internal sealed class CountingFactory(SshConnectionOptions? options) : ISshSessionFactory
    {
        private readonly SshSessionFactory _inner = new(options);
        public Action<ISshSession>? SessionOpened { get; set; }
        private int _opens;
        public int Opens => _opens;
        public ConcurrentBag<CountingSession> Sessions { get; } = [];
        public async Task<ISshSession> OpenAsync(SshSessionRequest request, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _opens);
            var session = new CountingSession(await _inner.OpenAsync(request, cancellationToken));
            Sessions.Add(session);
            SessionOpened?.Invoke(session);
            return session;
        }
    }

    internal sealed class CountingSession(ISshSession inner) : ISshSession
    {
        private readonly object _sync = new();
        private Task? _dispose;
        public bool IsDisposed { get; private set; }
        public Guid ServerId => inner.ServerId;
        public ForwardMode Mode => inner.Mode;
        public HostKeyIdentity HostKey => inner.HostKey;
        public CancellationToken Disconnected => inner.Disconnected;
        public Task<IForwardHandle> StartAsync(ForwardRule rule, CancellationToken cancellationToken = default) => inner.StartAsync(rule, cancellationToken);
        public ValueTask DisposeAsync()
        {
            lock (_sync)
            {
                if (_dispose is null || _dispose.IsFaulted) { _dispose = DisposeCoreAsync(); }
                return new(_dispose);
            }
        }
        private async Task DisposeCoreAsync() { await inner.DisposeAsync(); IsDisposed = true; }
    }
}

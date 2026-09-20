using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace TunnelDeck.Ssh.Tests.Fixture;

/// <summary>Allows probe + authentication handshakes, and can delay just one authenticated transport's replies.</summary>
internal sealed class MultiConnectionSshProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<int, Peer> _peers = new();
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly Channel<bool> _held = Channel.CreateUnbounded<bool>();
    private readonly string _host;
    private int _port;
    private readonly Task _accept;
    private int _nextId;
    private int _rejecting;

    public MultiConnectionSshProxy(string host, int port)
    {
        _host = host;
        _port = port;
        _listener.Start();
        _accept = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public void SetUpstreamPort(int port) => Volatile.Write(ref _port, port);
    public void RejectConnections(bool reject) => Volatile.Write(ref _rejecting, reject ? 1 : 0);
    public void DisconnectAll() { foreach (Peer peer in _peers.Values) { peer.Disconnect(); } }
    public void HoldNewestReplies() => _peers.MaxBy(pair => pair.Key).Value.Hold = true;
    public void BlackholeNewest() => _peers.MaxBy(pair => pair.Key).Value.Blackhole = true;
    public void ResumeReplies() { foreach (Peer peer in _peers.Values) { peer.Hold = false; peer.Release?.TrySetResult(); } }
    public async Task WaitForHeldReplyAsync() => await _held.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    private async Task AcceptAsync()
    {
        try
        {
            while (true)
            {
                TcpClient downstream = await _listener.AcceptTcpClientAsync(_stopping.Token);
                if (Volatile.Read(ref _rejecting) != 0) { downstream.Dispose(); continue; }
                var peer = new Peer(Interlocked.Increment(ref _nextId));
                _peers[peer.Id] = peer;
                _connections.Add(RelayAsync(peer, downstream));
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
    }

    private async Task RelayAsync(Peer peer, TcpClient downstream)
    {
        using (downstream)
        using (var upstream = new TcpClient())
        using (peer)
        using (var stopping = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token, peer.Disconnected))
        {
            try
            {
                await upstream.ConnectAsync(_host, Volatile.Read(ref _port), stopping.Token);
                Task upload = PumpAsync(peer, downstream.GetStream(), upstream.GetStream(), false, stopping.Token);
                Task download = PumpAsync(peer, upstream.GetStream(), downstream.GetStream(), true, stopping.Token);
                await Task.WhenAny(upload, download);
                // A network blackhole does not deliver the client's FIN to sshd. Keep the
                // upstream socket alive until sshd itself detects the unanswered probes.
                if (peer.Blackhole) { await download; }
                await stopping.CancelAsync();
                await Task.WhenAll(upload, download);
            }
            catch (Exception error) when (error is OperationCanceledException or IOException or SocketException) { }
            finally { _peers.TryRemove(peer.Id, out _); }
        }
    }

    private async Task PumpAsync(Peer peer, NetworkStream source, NetworkStream destination, bool canHold, CancellationToken token)
    {
        byte[] buffer = new byte[65536];
        int count;
        while ((count = await source.ReadAsync(buffer, token)) > 0)
        {
            if (peer.Blackhole) { continue; }
            if (canHold && peer.Hold)
            {
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                peer.Release = release;
                if (peer.Hold)
                {
                    _held.Writer.TryWrite(true);
                    await release.Task.WaitAsync(token);
                }
            }
            await destination.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        ResumeReplies();
        _listener.Stop();
        await _accept;
        await Task.WhenAll(_connections).WaitAsync(TimeSpan.FromSeconds(5));
        _stopping.Dispose();
    }

    private sealed class Peer(int id) : IDisposable
    {
        private readonly object _sync = new();
        private readonly CancellationTokenSource _disconnected = new();
        private bool _disposed;
        public int Id { get; } = id;
        public volatile bool Hold;
        public volatile bool Blackhole;
        public TaskCompletionSource? Release;
        public CancellationToken Disconnected => _disconnected.Token;
        public void Disconnect() { lock (_sync) { if (!_disposed) { _disconnected.Cancel(); } } }
        public void Dispose() { lock (_sync) { _disposed = true; _disconnected.Dispose(); } }
    }
}

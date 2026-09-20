using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;

namespace TunnelDeck.Ssh.Tests.Fixture;

/// <summary>Holds encrypted server-to-client bytes without impersonating an SSH endpoint.</summary>
public sealed class ControlledSshProxy : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Channel<TaskCompletionSource> _heldChunks = Channel.CreateUnbounded<TaskCompletionSource>();
    private readonly Task _run;
    private readonly string _upstreamHost;
    private readonly int _upstreamPort;
    private TaskCompletionSource? _currentChunk;
    private int _holding;

    public ControlledSshProxy(string upstreamHost, int upstreamPort)
    {
        _upstreamHost = upstreamHost;
        _upstreamPort = upstreamPort;
        _listener.Start();
        _run = RunAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public void HoldServerReplies() => Interlocked.Exchange(ref _holding, 1);

    public async Task<TaskCompletionSource> WaitForHeldChunkAsync() =>
        await _heldChunks.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

    public void ResumeServerReplies()
    {
        Interlocked.Exchange(ref _holding, 0);
        Volatile.Read(ref _currentChunk)?.TrySetResult();
    }

    private async Task RunAsync()
    {
        try
        {
            using TcpClient downstream = await _listener.AcceptTcpClientAsync(_stopping.Token);
            using var upstream = new TcpClient();
            await upstream.ConnectAsync(_upstreamHost, _upstreamPort, _stopping.Token);
            Task toServer = PumpAsync(downstream.GetStream(), upstream.GetStream(), hold: false);
            Task fromServer = PumpAsync(upstream.GetStream(), downstream.GetStream(), hold: true);
            await Task.WhenAny(toServer, fromServer);
            await _stopping.CancelAsync();
            ResumeServerReplies();
            await Task.WhenAll(toServer, fromServer);
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (IOException) { }
        catch (SocketException) when (_stopping.IsCancellationRequested) { }
    }

    private async Task PumpAsync(NetworkStream source, NetworkStream destination, bool hold)
    {
        byte[] buffer = new byte[65536];
        int count;
        while ((count = await source.ReadAsync(buffer, _stopping.Token)) > 0)
        {
            if (hold && Volatile.Read(ref _holding) != 0)
            {
                var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Volatile.Write(ref _currentChunk, release);
                if (Volatile.Read(ref _holding) != 0)
                {
                    await _heldChunks.Writer.WriteAsync(release, _stopping.Token);
                    await release.Task.WaitAsync(_stopping.Token);
                }
            }
            await destination.WriteAsync(buffer.AsMemory(0, count), _stopping.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        ResumeServerReplies();
        _listener.Stop();
        await _run.WaitAsync(TimeSpan.FromSeconds(5));
        _stopping.Dispose();
    }
}

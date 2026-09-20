using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace TunnelDeck.Ssh.Tests.Fixture;

public sealed class LocalEchoServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentBag<Task> _connections = [];
    private readonly Task _acceptLoop;

    public LocalEchoServer(IPAddress? address = null, int port = 0)
    {
        _listener = new TcpListener(address ?? IPAddress.Loopback, port);
        _listener.Start();
        _acceptLoop = AcceptAsync();
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    private async Task AcceptAsync()
    {
        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(_stopping.Token);
                _connections.Add(EchoAsync(client));
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
        catch (SocketException) when (_stopping.IsCancellationRequested) { }
    }

    private async Task EchoAsync(TcpClient client)
    {
        using (client)
        {
            byte[] buffer = new byte[65536];
            try
            {
                NetworkStream stream = client.GetStream();
                int count;
                while ((count = await stream.ReadAsync(buffer, _stopping.Token)) > 0)
                {
                    await stream.WriteAsync(buffer.AsMemory(0, count), _stopping.Token);
                }
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
            catch (IOException) { }
        }
    }

    public static async Task AssertPayloadAsync(int port, byte[] payload, IPAddress? address = null)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var client = new TcpClient((address ?? IPAddress.Loopback).AddressFamily);
        await client.ConnectAsync(address ?? IPAddress.Loopback, port, deadline.Token);
        NetworkStream stream = client.GetStream();
        Task write = stream.WriteAsync(payload, deadline.Token).AsTask();
        byte[] actual = new byte[payload.Length];
        await Task.WhenAll(write, stream.ReadExactlyAsync(actual, deadline.Token).AsTask());
        Assert.Equal(payload, actual);
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();
        await _acceptLoop;
        await Task.WhenAll(_connections);
        _stopping.Dispose();
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TunnelDeck.Ssh.Tests.Fixture;

internal sealed class LocalHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentBag<Task> _clients = [];
    private readonly Task _accept;
    public byte[] Response { get; }
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;
    public static byte[] Request => Encoding.ASCII.GetBytes("GET /fixture HTTP/1.1\r\nHost: fixture\r\nConnection: close\r\n\r\n");

    public LocalHttpServer(byte[] body)
    {
        Response = [.. Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n"), .. body];
        _listener.Start();
        _accept = AcceptAsync();
    }
    private async Task AcceptAsync()
    {
        try { while (true) { _clients.Add(ReplyAsync(await _listener.AcceptTcpClientAsync(_stopping.Token))); } }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested) { }
    }
    private async Task ReplyAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                using var reader = new StreamReader(client.GetStream(), Encoding.ASCII, leaveOpen: true);
                while (await reader.ReadLineAsync(_stopping.Token) is { Length: > 0 }) { }
                await client.GetStream().WriteAsync(Response, _stopping.Token);
            }
            catch (Exception error) when (error is OperationCanceledException or IOException) { }
        }
    }
    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();
        await _accept;
        await Task.WhenAll(_clients);
        _stopping.Dispose();
    }
}

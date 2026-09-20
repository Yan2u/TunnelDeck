using System.Net;
using System.Net.Sockets;

namespace TunnelDeck.Ssh.Tests.Fixture;

/// <summary>Test-only reservations. Production port allocation never probes a port before binding it.</summary>
internal sealed class LocalPortBlock : IDisposable
{
    private readonly Socket?[] _sockets;
    public int First { get; }
    private LocalPortBlock(int first, Socket?[] sockets) { First = first; _sockets = sockets; }

    public static LocalPortBlock Reserve(int count)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            var sockets = new Socket?[count];
            try
            {
                // Stay outside both this test host's 1024–15000 dynamic TCP range and Windows'
                // default 49152–65535 range. Otherwise unrelated outbound HTTP/SSH connections
                // can acquire a just-released test listener's port and leave it in TIME_WAIT.
                int first = Random.Shared.Next(20000, 30000 - count);
                for (int index = 0; index < count; index++)
                {
                    var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = false };
                    sockets[index] = socket;
                    socket.Bind(new IPEndPoint(IPAddress.Loopback, first + index));
                    socket.Listen(4);
                }
                return new(first, sockets);
            }
            catch (SocketException) { foreach (Socket? socket in sockets) { socket?.Dispose(); } }
        }
        throw new InvalidOperationException("Could not reserve a consecutive block of disposable test ports.");
    }

    public void Release(int index) { _sockets[index]?.Dispose(); _sockets[index] = null; }
    public void Dispose() { foreach (Socket? socket in _sockets) { socket?.Dispose(); } }

    public static void AssertReusable(int port)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = false };
        socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
        socket.Listen(1);
    }
}

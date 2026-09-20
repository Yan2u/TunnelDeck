using System.Net;
using System.Net.Sockets;

namespace TunnelDeck.Core.Models;

public sealed record NetworkEndpoint(string Host, int Port)
{
    public override string ToString() => IPAddress.TryParse(Host, out IPAddress? address) && address.AddressFamily == AddressFamily.InterNetworkV6
        ? $"[{address}]:{Port}" : $"{Host}:{Port}";
}

using System.Collections.Immutable;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Tests.Fixtures;

internal static class DomainData
{
    public static readonly DateTimeOffset Now = new(2026, 9, 19, 0, 0, 0, TimeSpan.Zero);
    public static Guid Id(int number) => new(number, 0, 0, new byte[8]);

    public static RemoteServer Server(int number = 1, string? name = null, DateTimeOffset? lastUsed = null) => new()
    {
        Id = Id(number), DisplayName = name, Host = $"server-{number}.example", Username = "dev", LastUsedAt = lastUsed,
    };

    public static ForwardRule Rule(int number = 1, int server = 1, ForwardMode mode = ForwardMode.Local,
        int localPort = 3000, int remotePort = 5432, bool auto = true) => new()
    {
        Id = Id(10000 + number), ServerId = Id(server), Mode = mode,
        LocalPort = localPort, RemotePort = remotePort, AutoFindAvailablePort = mode == ForwardMode.Local && auto,
    };

    public static ForwardProfile Profile(int number = 1, params ForwardRule[] rules) => new()
    {
        Id = Id(1000 + number), Name = $"Profile {number}", Rules = [.. rules],
    };

    public static AppState State(ImmutableArray<RemoteServer> servers, ImmutableArray<ForwardProfile> profiles) => new()
    {
        Servers = servers, Profiles = profiles,
        Order = new ResourceOrder
        {
            OtherServerIds = servers.Select(server => server.Id).ToImmutableArray(),
            OtherProfileIds = profiles.Select(profile => profile.Id).ToImmutableArray(),
        },
    };
}

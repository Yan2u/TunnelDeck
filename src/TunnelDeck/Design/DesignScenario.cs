using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Presentation;
using TunnelDeck.Core.Runtime;

namespace TunnelDeck.Design;

public enum DesignScenarioKind { Sample, Stress, Empty }
public sealed record DesignScenario(AppState State, ImmutableArray<ActiveTunnel> Tunnels);

public static class DesignScenarioFactory
{
    public static DesignScenario Create(DesignScenarioKind kind)
    {
        if (kind == DesignScenarioKind.Empty) { return new(new AppState(), []); }
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int count = kind == DesignScenarioKind.Stress ? 28 : 6;
        string[] serverNames = ["dev-linux", "database", "Staging", "Staging", "边缘节点 · 长名称与 IPv6 连接", "build-runner"];
        string[] profileNames = ["Web Development", "Database", "Backend services", "Web Development", "跨区域开发环境 · 长名称与多服务器转发", "Personal"];
        var servers = Enumerable.Range(1, count).Select(index => new RemoteServer
        {
            Id = Id(index), DisplayName = index <= serverNames.Length ? serverNames[index - 1] : $"Development server {index:00} · 长名称检查",
            Host = index == 5 ? "2001:db8:1234:5678:abcd:ef01:2345:6789" : index == 3 ? "staging-eu.example.com" : index == 4 ? "staging-us.example.com" : $"{(index == 1 ? "dev-linux" : "server-" + index)}.example.com",
            Username = index == 2 ? "dbadmin" : "ivy", Port = index == 5 ? 2222 : 22,
            AuthMethod = index == 2 ? AuthenticationMethod.PrivateKey : AuthenticationMethod.Password,
            IdentityFilePath = index == 2 ? "C:\\Keys\\development_ed25519" : null,
            LastUsedAt = now.AddMinutes(-index * 5),
        }).ToImmutableArray();
        var profiles = Enumerable.Range(1, count).Select(index => new ForwardProfile
        {
            Id = Id(1000 + index), Name = index <= profileNames.Length ? profileNames[index - 1] : $"Project workspace {index:00} · 开发配置",
            LastUsedAt = now.AddMinutes(-index * 7),
            Rules = index == 6 ? [] : Enumerable.Range(0, index == 1 ? 3 : 2).Select(rule => new ForwardRule
            {
                Id = Id(10000 + index * 10 + rule), ServerId = servers[(index + rule - 1) % count].Id,
                Mode = rule == 1 && index == 2 ? ForwardMode.Remote : ForwardMode.Local,
                LocalPort = rule == 0 ? 3000 : rule == 1 ? 5432 : 6379,
                RemotePort = rule == 0 ? 3000 : rule == 1 ? 5432 : 6379,
                AutoFindAvailablePort = !(rule == 1 && index == 2),
            }).ToImmutableArray(),
        }).ToImmutableArray();
        int pinned = kind == DesignScenarioKind.Stress ? 14 : 2;
        var state = new AppState
        {
            Servers = servers, Profiles = profiles,
            Order = new()
            {
                PinnedServerIds = servers.Take(pinned).Select(server => server.Id).ToImmutableArray(),
                OtherServerIds = servers.Skip(pinned).Select(server => server.Id).ToImmutableArray(),
                PinnedProfileIds = profiles.Take(pinned).Select(profile => profile.Id).ToImmutableArray(),
                OtherProfileIds = profiles.Skip(pinned).Select(profile => profile.Id).ToImmutableArray(),
            },
            Settings = new() { DefaultServerId = servers[0].Id, PinnedQuickServerId = servers[0].Id, SidebarMaximumItems = kind == DesignScenarioKind.Stress ? 10 : 5 },
            Statistics = new()
            {
                TotalForwards = 128,
                ServerForwards = ImmutableDictionary<Guid, long>.Empty.Add(servers[0].Id, 84).Add(servers[1].Id, 44),
                ProfileUses = ImmutableDictionary<Guid, long>.Empty.Add(profiles[0].Id, 37).Add(profiles[1].Id, 12),
            },
            RecentActivity = Enumerable.Range(0, 3).Select(index => new ActivityEntry
            {
                Id = Id(20000 + index), OccurredAt = now.AddMinutes(-3 - index * 11), Outcome = ActivityOutcome.Stopped,
                Requested = profiles[0].Rules[index], Actual = profiles[0].Rules[index].GetRoute(),
                Source = new() { ServerName = ResourceDisplay.ServerName(servers[index]), ProfileId = profiles[0].Id, ProfileName = profiles[0].Name, RuleId = profiles[0].Rules[index].Id },
            }).ToImmutableArray(),
        };
        ForwardRule local = profiles[0].Rules[0];
        ForwardRule remote = profiles[1].Rules[1];
        ImmutableArray<ActiveTunnel> tunnels =
        [
            new() { Id = Id(30000), Requested = local, Actual = local.GetRoute() with { Local = new("127.0.0.1", 3001) }, State = TunnelState.Running, CreatedAt = now.AddMinutes(-8), StartedAt = now.AddMinutes(-8), AdjustmentReason = PortAdjustmentReason.LocalListenerInUse, Source = new() { ServerName = servers[0].DisplayName!, ProfileId = profiles[0].Id, ProfileName = profiles[0].Name } },
            new() { Id = Id(30001), Requested = profiles[0].Rules[1], Actual = profiles[0].Rules[1].GetRoute(), State = TunnelState.Running, CreatedAt = now.AddMinutes(-3), StartedAt = now.AddMinutes(-3), Source = new() { ServerName = servers[1].DisplayName!, ProfileId = profiles[0].Id, ProfileName = profiles[0].Name } },
            new() { Id = Id(30002), Requested = remote, Actual = remote.GetRoute(), State = TunnelState.Reconnecting, CreatedAt = now.AddMinutes(-5), StartedAt = now.AddMinutes(-5), Source = new() { ServerName = servers[2].DisplayName! } },
        ];
        if (kind == DesignScenarioKind.Stress)
        {
            tunnels = Enumerable.Range(0, 24).Select(index =>
            {
                ForwardRule rule = new()
                {
                    Id = Id(40000 + index), ServerId = servers[index % servers.Length].Id,
                    Mode = index % 4 == 0 ? ForwardMode.Remote : ForwardMode.Local,
                    LocalPort = 20000 + index * 2, RemotePort = 3000 + index, AutoFindAvailablePort = index % 4 != 0,
                };
                bool failed = index == 19;
                bool adjusted = index % 4 == 1;
                return new ActiveTunnel
                {
                    Id = Id(30000 + index), Requested = rule,
                    Actual = failed ? null : rule.GetRoute() with { Local = new(rule.LocalHost, rule.LocalPort + (adjusted ? 1 : 0)) },
                    State = failed ? TunnelState.Failed : TunnelState.Running,
                    CreatedAt = now.AddMinutes(-index - 1), StartedAt = failed ? null : now.AddMinutes(-index - 1),
                    EndedAt = failed ? now : null, AdjustmentReason = adjusted ? PortAdjustmentReason.LocalListenerInUse : null,
                    LastError = failed ? new(TunnelFailureKind.LocalAccessDenied, "Windows denied access to the requested listening port.") : null,
                    Source = new() { ServerName = ResourceDisplay.ServerName(servers[index % servers.Length]) },
                };
            }).ToImmutableArray();
        }
        return new(state, tunnels);
    }

    public static Guid Id(int number) => new(number, 0, 0, new byte[8]);
}

using System.Collections.Immutable;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Persistence;
using TunnelDeck.Infrastructure.Persistence;

namespace TunnelDeck.Infrastructure.Tests.Fixtures;

internal sealed class StorageFixture : IDisposable
{
    private static readonly string TestRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TunnelDeck.Tests"));
    public AppDataDirectory Directory { get; } = new(Path.Combine(TestRoot, Guid.NewGuid().ToString("N")));
    public MemoryCredentialStore Credentials { get; } = new();
    public JsonStateStore Store => new(Directory);
    public JsonCredentialCleanupStore Cleanup => new(Directory);

    public ConfigurationService Service(IStateStore? store = null, ICredentialStore? credentials = null) =>
        new(store ?? Store, credentials ?? Credentials, Cleanup, new FileConfigurationLock(Directory.TransactionLockPath));

    public static readonly DateTimeOffset Now = new(2026, 9, 19, 3, 0, 0, TimeSpan.Zero);
    public static Guid Id(int number) => new(number, 0, 0, new byte[8]);
    public static RemoteServer Server(int number = 1) => new()
    {
        Id = Id(number), DisplayName = "Same name", Host = $"server-{number}.example", Username = "dev",
        LastUsedAt = Now,
    };

    public static AppState CompleteState()
    {
        RemoteServer first = Server() with { CredentialReference = Id(900) };
        RemoteServer second = Server(2) with { AuthMethod = AuthenticationMethod.PrivateKey, IdentityFilePath = "C:\\fixture\\id_ed25519" };
        var local = new ForwardRule
        {
            Id = Id(100), ServerId = first.Id, Mode = ForwardMode.Local, LocalPort = 3000, RemotePort = 5432,
            AutoFindAvailablePort = true,
        };
        var remote = local with { Id = Id(101), ServerId = second.Id, Mode = ForwardMode.Remote, AutoFindAvailablePort = false };
        var profile = new ForwardProfile { Id = Id(10), Name = "Databases", Rules = [local, remote], LastUsedAt = Now };
        var empty = new ForwardProfile { Id = Id(11), Name = "Empty" };
        return new()
        {
            Servers = [first, second], Profiles = [profile, empty],
            Order = new() { PinnedServerIds = [first.Id], OtherServerIds = [second.Id], PinnedProfileIds = [profile.Id], OtherProfileIds = [empty.Id] },
            Settings = new() { DefaultServerId = first.Id, PinnedQuickServerId = second.Id, SidebarMaximumItems = 10, AutoFindAvailablePortDefault = false },
            Statistics = new()
            {
                TotalForwards = 42,
                ServerForwards = ImmutableDictionary<Guid, long>.Empty.Add(first.Id, 40).Add(Id(999), 2),
                ProfileUses = ImmutableDictionary<Guid, long>.Empty.Add(Id(998), 12),
            },
            RecentActivity =
            [
                new()
                {
                    Id = Id(500), OccurredAt = Now, Outcome = ActivityOutcome.Started,
                    Requested = local with { ServerId = Id(999) },
                    Actual = new(new("127.0.0.1", 3001), new("127.0.0.1", 5432)),
                    Source = new() { ServerName = "Deleted server", ProfileId = Id(998), ProfileName = "Deleted profile", RuleId = local.Id },
                },
                new()
                {
                    Id = Id(501), OccurredAt = Now.AddSeconds(-1), Outcome = ActivityOutcome.Failed, Requested = remote,
                    Source = new() { ServerName = "Server snapshot" },
                    Error = new(TunnelFailureKind.RemoteListenFailure, "Remote listening failed."),
                },
            ],
        };
    }

    public void Dispose()
    {
        string target = Path.GetFullPath(Directory.RootPath);
        if (!target.StartsWith(TestRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(target), "N", out _))
        {
            throw new InvalidOperationException("Refusing to delete a path outside the disposable fixture.");
        }
        if (System.IO.Directory.Exists(target)) { System.IO.Directory.Delete(target, recursive: true); }
    }
}

internal sealed class MemoryCredentialStore : ICredentialStore
{
    public Dictionary<Guid, string> Values { get; } = [];
    public List<Guid> Deleted { get; } = [];
    public Action<Guid, string>? OnWrite { get; set; }
    public Action<Guid>? OnDelete { get; set; }

    public string? Read(Guid reference) => Values.GetValueOrDefault(reference);
    public void Write(Guid reference, string secret)
    {
        Values[reference] = secret;
        OnWrite?.Invoke(reference, secret);
    }
    public void Delete(Guid reference)
    {
        OnDelete?.Invoke(reference);
        Values.Remove(reference);
        Deleted.Add(reference);
    }
}

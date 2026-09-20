using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Snapshots;

public static class ProfileSnapshotFactory
{
    public static ProfileLaunchSnapshot Capture(ForwardProfile profile, IEnumerable<RemoteServer> servers, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(servers);
        Dictionary<Guid, RemoteServer> byId = servers.ToDictionary(server => server.Id);
        ConfigurationValidator.ValidateProfile(profile, byId.Keys.ToHashSet()).ThrowIfInvalid();
        ImmutableArray<RuleLaunchSnapshot> rules = profile.Rules.Select(rule =>
        {
            RemoteServer server = byId[rule.ServerId];
            ConfigurationValidator.ValidateServer(server).ThrowIfInvalid();
            string serverName = string.IsNullOrWhiteSpace(server.DisplayName)
                ? $"{server.Username}@{new NetworkEndpoint(server.Host, server.Port)}" : server.DisplayName;
            return new RuleLaunchSnapshot(server with { }, rule with { }, new ForwardSourceSnapshot
            {
                ProfileId = profile.Id, ProfileName = profile.Name, RuleId = rule.Id, ServerName = serverName,
            });
        }).ToImmutableArray();
        return new(profile.Id, profile.Name, (timeProvider ?? TimeProvider.System).GetUtcNow().ToUniversalTime(), rules);
    }
}

using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Transfer;

public enum ExportScope { Servers, Profiles, Everything }

/// <summary>Profiles always travel with the non-secret description of every server they reference.</summary>
public static class ExportBuilder
{
    public static TunnelDeckExport Build(AppState state, ExportScope scope, DateTimeOffset exportedAt,
        IReadOnlyCollection<Guid>? profileIds = null, IReadOnlyCollection<Guid>? serverIds = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Enum.IsDefined(scope)) { throw new ArgumentOutOfRangeException(nameof(scope)); }
        AppStateValidator.Validate(state).ThrowIfInvalid();
        ImmutableArray<ForwardProfile> profiles = scope == ExportScope.Servers ? []
            : state.Profiles.Where(profile => profileIds is null || profileIds.Contains(profile.Id)).ToImmutableArray();
        var required = new HashSet<Guid>(profiles.SelectMany(profile => profile.Rules).Select(rule => rule.ServerId));
        ImmutableArray<RemoteServer> servers = state.Servers
            .Where(server => scope != ExportScope.Profiles && (serverIds is null || serverIds.Contains(server.Id)) || required.Contains(server.Id))
            .ToImmutableArray();
        var exportedServers = servers.Select(server => server.Id).ToHashSet();
        var exportedProfiles = profiles.Select(profile => profile.Id).ToHashSet();
        return new()
        {
            ExportedAt = exportedAt.ToUniversalTime(),
            Servers = servers.Select(ExportedServer.From).ToImmutableArray(),
            Profiles = profiles.Select(ExportedProfile.From).ToImmutableArray(),
            Order = new()
            {
                PinnedServerIds = [.. state.Order.PinnedServerIds.Where(exportedServers.Contains)],
                OtherServerIds = [.. state.Order.OtherServerIds.Where(exportedServers.Contains)],
                PinnedProfileIds = [.. state.Order.PinnedProfileIds.Where(exportedProfiles.Contains)],
                OtherProfileIds = [.. state.Order.OtherProfileIds.Where(exportedProfiles.Contains)],
            },
        };
    }
}

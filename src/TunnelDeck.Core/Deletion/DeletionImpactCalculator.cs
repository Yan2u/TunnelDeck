using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Operations;
using TunnelDeck.Core.Policies;
using TunnelDeck.Core.Runtime;
using TunnelDeck.Core.Tunnels;

namespace TunnelDeck.Core.Deletion;

/// <summary>Single and bulk deletion share this ID-based dependency scan; names are display snapshots only.</summary>
public static class DeletionImpactCalculator
{
    public static DeletionImpact Calculate(AppState state, TunnelManagerSnapshot runtime, ServerWorkRegistrySnapshot pending, DeletionRequest request)
    {
        var selected = request.All
            ? (request.Kind == ResourceKind.Server ? state.Servers.Select(server => server.Id) : state.Profiles.Select(profile => profile.Id)).ToHashSet()
            : request.Ids.ToHashSet();
        ImmutableArray<RemoteServer> servers = request.Kind == ResourceKind.Server
            ? [.. state.Servers.Where(server => selected.Contains(server.Id)).OrderBy(server => server.Id)] : [];
        ImmutableArray<AffectedProfile> profiles = request.Kind == ResourceKind.Server
            ? [.. state.Profiles.Select(profile => new AffectedProfile(profile.Id, profile.Name, [.. profile.Rules.Where(rule => selected.Contains(rule.ServerId))]))
                .Where(profile => !profile.Rules.IsEmpty).OrderBy(profile => profile.Id)]
            : [.. state.Profiles.Where(profile => selected.Contains(profile.Id)).OrderBy(profile => profile.Id)
                .Select(profile => new AffectedProfile(profile.Id, profile.Name, profile.Rules))];
        ImmutableArray<ActiveTunnel> tunnels = [.. runtime.Tunnels.Where(tunnel => IsLive(tunnel) &&
            (request.Kind == ResourceKind.Server ? selected.Contains(tunnel.ServerId) :
                tunnel.Source.ProfileId is Guid id && selected.Contains(id))).OrderBy(tunnel => tunnel.Id)];
        ImmutableArray<ServerWorkSnapshot> work = [.. pending.Work
            .Where(work => request.Kind == ResourceKind.Server ? selected.Contains(work.Server.Id) :
                work.Forwards.Any(rule => rule.Launch.Source.ProfileId is Guid id && selected.Contains(id)))
            .Where(work => work.Kind == ServerWorkKind.ConnectionTest || !work.Forwards.IsEmpty)
            .OrderBy(work => work.Id)];
        return new(request, state.Revision, runtime.Revision, pending.Revision, servers, profiles, tunnels, work,
            request.Kind == ResourceKind.Server && state.Settings.DefaultServerId is Guid defaultId && selected.Contains(defaultId),
            request.Kind == ResourceKind.Server && state.Settings.PinnedQuickServerId is Guid quickId && selected.Contains(quickId));
    }

    public static bool IsLive(ActiveTunnel tunnel) =>
        tunnel.State is TunnelState.Starting or TunnelState.Running or TunnelState.Stopping or TunnelState.Reconnecting;

    /// <summary>Traffic counters, timestamps and unrelated edits do not make a confirmation stale.</summary>
    public static bool SameEffect(DeletionImpact before, DeletionImpact after)
    {
        if (before.Request.Kind != after.Request.Kind || before.Request.All != after.Request.All ||
            !before.Request.Ids.SequenceEqual(after.Request.Ids) ||
            before.ClearsDefaultServer != after.ClearsDefaultServer || before.ClearsPinnedQuickServer != after.ClearsPinnedQuickServer ||
            !before.Servers.Select(server => server with { LastUsedAt = null }).SequenceEqual(after.Servers.Select(server => server with { LastUsedAt = null })) ||
            before.Profiles.Length != after.Profiles.Length || before.Tunnels.Length != after.Tunnels.Length ||
            before.Work.Length != after.Work.Length) { return false; }
        if (!before.Profiles.Zip(after.Profiles).All(pair => pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name &&
            pair.First.Rules.SequenceEqual(pair.Second.Rules))) { return false; }
        if (!before.Tunnels.Zip(after.Tunnels).All(pair => pair.First.Id == pair.Second.Id && pair.First.Generation == pair.Second.Generation &&
            pair.First.State == pair.Second.State && pair.First.StopRequested == pair.Second.StopRequested &&
            pair.First.Requested == pair.Second.Requested && pair.First.Source == pair.Second.Source)) { return false; }
        return before.Work.Zip(after.Work).All(pair => pair.First.Id == pair.Second.Id && pair.First.Kind == pair.Second.Kind &&
            (pair.First.Server with { LastUsedAt = null }) == (pair.Second.Server with { LastUsedAt = null }) &&
            pair.First.Forwards.SequenceEqual(pair.Second.Forwards));
    }

    public static AppState Remove(AppState state, DeletionImpact impact)
    {
        ResourceOrder order = state.Order;
        if (impact.Request.Kind == ResourceKind.Profile)
        {
            var ids = impact.Profiles.Select(profile => profile.Id).ToHashSet();
            foreach (Guid id in ids) { order = ResourceOrderService.Remove(order, ResourceKind.Profile, id); }
            return state with { Profiles = [.. state.Profiles.Where(profile => !ids.Contains(profile.Id))], Order = order };
        }
        var servers = impact.Servers.Select(server => server.Id).ToHashSet();
        foreach (Guid id in servers) { order = ResourceOrderService.Remove(order, ResourceKind.Server, id); }
        return state with
        {
            Servers = [.. state.Servers.Where(server => !servers.Contains(server.Id))],
            Profiles = [.. state.Profiles.Select(profile => profile with { Rules = [.. profile.Rules.Where(rule => !servers.Contains(rule.ServerId))] })],
            Order = order,
            Settings = state.Settings with
            {
                DefaultServerId = state.Settings.DefaultServerId is Guid defaultId && servers.Contains(defaultId) ? null : state.Settings.DefaultServerId,
                PinnedQuickServerId = state.Settings.PinnedQuickServerId is Guid quickId && servers.Contains(quickId) ? null : state.Settings.PinnedQuickServerId,
            },
        };
    }
}

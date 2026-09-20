using System.Collections.Immutable;
using TunnelDeck.Core.Abstractions;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Snapshots;

namespace TunnelDeck.Core.Tunnels;

public sealed partial class TunnelManager
{
    public async Task<ImmutableArray<TunnelBatchItem>> StartAllAsync(IEnumerable<RuleLaunchSnapshot> rules,
        IReadOnlyDictionary<Guid, ConnectionCredentials>? credentials = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rules);
        RuleLaunchSnapshot[] snapshot = rules.Select(rule => rule with
        {
            Server = rule.Server with { }, Requested = rule.Requested with { }, Source = rule.Source with { },
        }).ToArray();
        var inputs = credentials?.ToImmutableDictionary();
        return [.. await Task.WhenAll(snapshot.Select(async rule =>
        {
            try
            {
                TunnelStartResult result = await StartAsync(rule, inputs?.GetValueOrDefault(rule.Server.Id), cancellationToken).ConfigureAwait(false);
                return new TunnelBatchItem(rule.Requested, result, result.Succeeded ? null : result.Tunnel.LastError);
            }
            catch (Exception error) { return new TunnelBatchItem(rule.Requested, null, TunnelErrors.From(error)); }
        })).ConfigureAwait(false)];
    }
}

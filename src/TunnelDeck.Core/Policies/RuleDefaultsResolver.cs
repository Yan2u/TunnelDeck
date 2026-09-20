using TunnelDeck.Core.Drafts;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Policies;

public static class RuleDefaultsResolver
{
    public static Guid? ResolveServerId(IReadOnlySet<Guid> availableServers, AppSettings settings,
        Guid? previousRuleServerId = null, Guid? currentQuickServerId = null)
    {
        ArgumentNullException.ThrowIfNull(availableServers);
        ArgumentNullException.ThrowIfNull(settings);
        Guid?[] candidates = [previousRuleServerId, currentQuickServerId, settings.PinnedQuickServerId, settings.DefaultServerId];
        return candidates.FirstOrDefault(id => id is Guid value && value != Guid.Empty && availableServers.Contains(value));
    }

    public static ForwardRuleDraft CreateRuleDraft(IReadOnlySet<Guid> availableServers, AppSettings settings,
        ForwardProfile? profile = null, Guid? currentQuickServerId = null, ForwardMode mode = ForwardMode.Local) => new()
    {
        ServerId = ResolveServerId(availableServers, settings, profile?.Rules.LastOrDefault()?.ServerId, currentQuickServerId),
        Mode = mode,
        AutoFindAvailablePort = settings.AutoFindAvailablePortDefault,
    };
}

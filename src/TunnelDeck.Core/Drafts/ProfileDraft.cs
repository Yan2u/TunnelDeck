using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Drafts;

public sealed class ProfileDraft
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public List<ForwardRuleDraft> Rules { get; } = [];
    public DateTimeOffset? LastUsedAt { get; set; }

    public static ProfileDraft FromProfile(ForwardProfile profile)
    {
        var draft = new ProfileDraft { Id = profile.Id, Name = profile.Name, LastUsedAt = profile.LastUsedAt };
        draft.Rules.AddRange(profile.Rules.Select(ForwardRuleDraft.FromRule));
        return draft;
    }

    public ForwardProfile Build(IReadOnlySet<Guid>? availableServers = null)
    {
        var profile = new ForwardProfile
        {
            Id = Id, Name = Name?.Trim() ?? string.Empty, LastUsedAt = LastUsedAt,
            Rules = Rules.Select(rule => rule.ToCandidate()).ToImmutableArray(),
        };
        ConfigurationValidator.ValidateProfile(profile, availableServers).ThrowIfInvalid();
        return profile;
    }
}

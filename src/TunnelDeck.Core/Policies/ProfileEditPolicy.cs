using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Policies;

public static class ProfileEditPolicy
{
    // Usage timestamps can advance while a form is open; only editable content conflicts.
    public static bool ContentEquals(ForwardProfile? first, ForwardProfile? second) =>
        first is null || second is null ? first is null && second is null :
        first.Id == second.Id && first.Name == second.Name && first.Rules.SequenceEqual(second.Rules);
}

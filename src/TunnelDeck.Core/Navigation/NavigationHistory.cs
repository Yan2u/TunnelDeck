using System.Collections.Immutable;
using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Navigation;

/// <summary>Resource identity, not page titles, determines navigation. One history owner per window.</summary>
public sealed class NavigationHistory
{
    public AppRoute Current { get; private set; } = AppRoute.Dashboard;
    public ImmutableArray<AppRoute> BackStack { get; private set; } = [];
    public bool CanGoBack => !BackStack.IsEmpty;

    public bool Navigate(AppRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route == Current) { return false; }
        BackStack = BackStack.Add(Current);
        Current = route;
        return true;
    }

    public bool Replace(AppRoute route)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (route == Current) { return false; }
        Current = route;
        RemoveAdjacentCurrent();
        return true;
    }

    public bool ReplaceIfCurrent(AppRoute expected, AppRoute replacement) => Current == expected && Replace(replacement);

    public bool GoBack()
    {
        if (BackStack.IsEmpty) { return false; }
        Current = BackStack[^1];
        BackStack = BackStack.RemoveAt(BackStack.Length - 1);
        RemoveAdjacentCurrent();
        return true;
    }

    public bool Reconcile(AppState state)
    {
        BackStack = BackStack.Where(route => route.ExistsIn(state)).ToImmutableArray();
        bool changed = !Current.ExistsIn(state);
        if (changed) { Current = Current.Parent; }
        RemoveAdjacentCurrent();
        return changed;
    }

    private void RemoveAdjacentCurrent()
    {
        while (!BackStack.IsEmpty && BackStack[^1] == Current) { BackStack = BackStack.RemoveAt(BackStack.Length - 1); }
    }
}

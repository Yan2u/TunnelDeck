using System.Collections.Immutable;
using TunnelDeck.Core.Models;
using TunnelDeck.Core.Runtime;

namespace TunnelDeck.Core.Tunnels;

public sealed record TunnelManagerSnapshot(long Revision, ImmutableArray<ActiveTunnel> Tunnels)
{
    public int RunningCount => Tunnels.Count(tunnel => tunnel.IsRunning);
}

public sealed record TunnelStartResult(ActiveTunnel Tunnel, bool Reused)
{
    public bool Succeeded => Tunnel.State == TunnelState.Running;
}

public sealed record TunnelStopResult(Guid TunnelId, bool Succeeded, ActiveTunnel? Tunnel, TunnelFailure? Failure = null);

public sealed record TunnelBatchItem(ForwardRule Requested, TunnelStartResult? Result, TunnelFailure? Failure)
{
    public bool Succeeded => Result?.Succeeded == true;
}

public sealed record TunnelManagerOptions
{
    public int MaximumConcurrentStarts { get; init; } = 4;
    public int MaximumConcurrentCleanup { get; init; } = 4;
    public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan DiagnosticNotificationInterval { get; init; } = TimeSpan.FromSeconds(2);

    internal void Validate()
    {
        if (MaximumConcurrentStarts < 1 || MaximumConcurrentCleanup < 1 || StopTimeout <= TimeSpan.Zero || DiagnosticNotificationInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException(UiText.Invariant("Diagnostic_TunnelResults_TunnelConcurrencyLimitsAndTimeBudgetsMustBe"));
        }
    }
}

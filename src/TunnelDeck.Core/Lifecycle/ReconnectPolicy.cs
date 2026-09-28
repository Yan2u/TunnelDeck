using TunnelDeck.Core.Models;

namespace TunnelDeck.Core.Lifecycle;

public sealed record ReconnectOptions
{
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaximumDelay { get; init; } = TimeSpan.FromSeconds(30);
    public TimeSpan RecoveryWindow { get; init; } = TimeSpan.FromMinutes(2);
    public double JitterFraction { get; init; } = 0.2;

    internal void Validate()
    {
        if (InitialDelay <= TimeSpan.Zero || MaximumDelay < InitialDelay || RecoveryWindow <= TimeSpan.Zero ||
            !double.IsFinite(JitterFraction) || JitterFraction is < 0 or > 1)
        { throw new ArgumentException(UiText.Invariant("Diagnostic_ReconnectPolicy_ReconnectDelaysAndRecoveryWindowMustBePositive")); }
    }
}

public sealed class ReconnectPolicy
{
    private readonly ReconnectOptions _options;
    private readonly Func<double> _random;
    public ReconnectPolicy(ReconnectOptions? options = null, Func<double>? random = null)
    { _options = options ?? new(); _options.Validate(); _random = random ?? Random.Shared.NextDouble; }
    public TimeSpan RecoveryWindow => _options.RecoveryWindow;

    public TimeSpan Delay(int completedRounds, TimeSpan remaining)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(completedRounds);
        if (remaining <= TimeSpan.Zero) { return TimeSpan.Zero; }
        double exponential = Math.Min(_options.MaximumDelay.TotalMilliseconds,
            _options.InitialDelay.TotalMilliseconds * Math.Pow(2, Math.Min(completedRounds, 30)));
        double sample = Math.Clamp(_random(), 0, 1);
        double jittered = exponential * (1 + (sample * 2 - 1) * _options.JitterFraction);
        return TimeSpan.FromMilliseconds(Math.Min(remaining.TotalMilliseconds,
            Math.Min(_options.MaximumDelay.TotalMilliseconds, Math.Max(1, jittered))));
    }

    public static bool CanRetry(TunnelFailure? failure) => failure?.Kind is
        TunnelFailureKind.Connectivity or TunnelFailureKind.TimeoutUnknownOutcome;
}

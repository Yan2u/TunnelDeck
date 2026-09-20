using TunnelDeck.Core.Models;
using TunnelDeck.Core.Validation;

namespace TunnelDeck.Core.Policies;

public sealed class PortSearchPolicy
{
    public const int MaximumAttempts = 20;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _totalTimeout;
    private readonly TimeSpan _attemptTimeout;

    public PortSearchPolicy(TimeProvider? timeProvider = null, TimeSpan? totalTimeout = null, TimeSpan? attemptTimeout = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _totalTimeout = totalTimeout ?? TimeSpan.FromSeconds(10);
        _attemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(2);
        if (_totalTimeout <= TimeSpan.Zero || _attemptTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(totalTimeout), "Port-search timeouts must be positive.");
        }
    }

    public PortSearch Begin(ForwardRule requested, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requested);
        // Defense in depth: malformed/legacy Remote flags never enable a remote retry.
        ConfigurationValidator.ValidateRule(requested with
        {
            AutoFindAvailablePort = requested.Mode == ForwardMode.Local && requested.AutoFindAvailablePort,
        }).ThrowIfInvalid();
        return new PortSearch(requested with { }, _timeProvider, _totalTimeout, _attemptTimeout, cancellationToken);
    }
}

public enum PortSearchStopReason
{
    None,
    Completed,
    RetryDisabled,
    NonRetryableFailure,
    AttemptLimitReached,
    PortLimitReached,
    DeadlineExceeded,
    Cancelled,
}

public sealed record PortSearchAttempt(int Number, ForwardRule Candidate, TimeSpan Timeout);

/// <summary>Chooses attempts only. The network layer must apply each timeout/token and dispose failed handles.</summary>
public sealed class PortSearch
{
    private readonly TimeProvider _clock;
    private readonly long _started;
    private readonly TimeSpan _totalTimeout;
    private readonly TimeSpan _attemptTimeout;
    private readonly CancellationToken _cancellationToken;

    internal PortSearch(ForwardRule requested, TimeProvider clock, TimeSpan totalTimeout, TimeSpan attemptTimeout, CancellationToken cancellationToken)
    {
        Requested = requested;
        _clock = clock;
        _started = clock.GetTimestamp();
        _totalTimeout = totalTimeout;
        _attemptTimeout = attemptTimeout;
        _cancellationToken = cancellationToken;
    }

    public ForwardRule Requested { get; }
    public int AttemptsIssued { get; private set; }
    public PortSearchStopReason StopReason { get; private set; }

    /// <param name="previousFailure">Null requests the initial attempt, or marks a previous attempt successful.</param>
    public PortSearchAttempt? Next(TunnelFailureKind? previousFailure = null)
    {
        if (StopReason != PortSearchStopReason.None) { return null; }
        if (AttemptsIssued == 0 && previousFailure is not null) { throw new ArgumentException("There is no previous attempt.", nameof(previousFailure)); }
        if (AttemptsIssued > 0 && previousFailure is null) { return Stop(PortSearchStopReason.Completed); }
        if (_cancellationToken.IsCancellationRequested) { return Stop(PortSearchStopReason.Cancelled); }
        TimeSpan remaining = _totalTimeout - _clock.GetElapsedTime(_started);
        if (remaining <= TimeSpan.Zero) { return Stop(PortSearchStopReason.DeadlineExceeded); }
        if (AttemptsIssued > 0)
        {
            if (Requested.Mode != ForwardMode.Local || !Requested.AutoFindAvailablePort) { return Stop(PortSearchStopReason.RetryDisabled); }
            if (previousFailure != TunnelFailureKind.LocalAddressInUse) { return Stop(PortSearchStopReason.NonRetryableFailure); }
            if (AttemptsIssued >= PortSearchPolicy.MaximumAttempts) { return Stop(PortSearchStopReason.AttemptLimitReached); }
            if (Requested.LocalPort + AttemptsIssued > PortNumber.Maximum) { return Stop(PortSearchStopReason.PortLimitReached); }
        }
        ForwardRule candidate = Requested with
        {
            LocalPort = Requested.Mode == ForwardMode.Local ? Requested.LocalPort + AttemptsIssued : Requested.LocalPort,
            AutoFindAvailablePort = Requested.Mode == ForwardMode.Local && Requested.AutoFindAvailablePort,
        };
        return new PortSearchAttempt(++AttemptsIssued, candidate, remaining < _attemptTimeout ? remaining : _attemptTimeout);
    }

    private PortSearchAttempt? Stop(PortSearchStopReason reason)
    {
        StopReason = reason;
        return null;
    }
}

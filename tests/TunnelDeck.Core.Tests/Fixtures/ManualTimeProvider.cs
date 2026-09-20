namespace TunnelDeck.Core.Tests.Fixtures;

internal sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => _ticks;
    public override DateTimeOffset GetUtcNow() => DomainData.Now.AddTicks(_ticks);

    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        _ticks = checked(_ticks + elapsed.Ticks);
    }
}

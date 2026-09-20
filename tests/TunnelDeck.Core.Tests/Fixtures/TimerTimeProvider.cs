namespace TunnelDeck.Core.Tests.Fixtures;

/// <summary>Virtual time and one-shot/periodic timers; no sleeps are needed to race deadlines against late completions.</summary>
internal sealed class TimerTimeProvider : TimeProvider
{
    private readonly object _sync = new();
    private readonly HashSet<TestTimer> _timers = [];
    private long _ticks;
    public TimeSpan? NextDue
    {
        get
        {
            lock (_sync)
            {
                long next = _timers.Where(timer => timer.Next >= 0).Select(timer => timer.Next).DefaultIfEmpty(long.MaxValue).Min();
                return next == long.MaxValue ? null : TimeSpan.FromTicks(next - _ticks);
            }
        }
    }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() { lock (_sync) { return _ticks; } }
    public override DateTimeOffset GetUtcNow() => DomainData.Now.AddTicks(GetTimestamp());
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new TestTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }

    public void Advance(TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(elapsed, TimeSpan.Zero);
        long target;
        lock (_sync) { target = checked(_ticks + elapsed.Ticks); }
        while (true)
        {
            TestTimer[] due;
            lock (_sync)
            {
                long next = _timers.Where(timer => timer.Next >= 0).Select(timer => timer.Next).DefaultIfEmpty(long.MaxValue).Min();
                if (next > target) { _ticks = target; return; }
                _ticks = next;
                due = _timers.Where(timer => timer.Next == next).ToArray();
                foreach (TestTimer timer in due) { timer.Next = timer.Period > 0 ? next + timer.Period : -1; }
            }
            foreach (TestTimer timer in due) { timer.Invoke(); }
        }
    }

    private sealed class TestTimer(TimerTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        private volatile bool _disposed;
        public long Next { get; set; } = -1;
        public long Period { get; private set; } = -1;
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._sync)
            {
                if (_disposed) { return false; }
                Next = dueTime == Timeout.InfiniteTimeSpan ? -1 : clock._ticks + dueTime.Ticks;
                Period = period == Timeout.InfiniteTimeSpan ? -1 : period.Ticks;
                clock._timers.Add(this);
                return true;
            }
        }
        public void Invoke() { if (!_disposed) { callback(state); } }
        public void Dispose() { lock (clock._sync) { _disposed = true; clock._timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}

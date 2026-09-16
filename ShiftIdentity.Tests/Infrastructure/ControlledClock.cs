namespace ShiftIdentity.Tests.Infrastructure;

public sealed class ControlledClock(DateTimeOffset now) : TimeProvider
{
    private long ticks = now.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);

    /// <summary>
    /// Whether a wait scheduled on this clock takes real time. Off by default: the response floor of a public
    /// security-email request is a delay on the injected clock, and a test that owns time through
    /// <see cref="Advance"/> learns nothing from sleeping through it. The real-clock timing tests turn it on.
    /// </summary>
    public bool RealDelays { get; set; }

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        base.CreateTimer(callback, state, RealDelays || dueTime == Timeout.InfiniteTimeSpan ? dueTime : TimeSpan.Zero, period);
}

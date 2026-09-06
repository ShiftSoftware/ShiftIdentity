namespace ShiftIdentity.Tests.Infrastructure;

public sealed class ControlledClock(DateTimeOffset now) : TimeProvider
{
    private long ticks = now.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref ticks), TimeSpan.Zero);
    public void Advance(TimeSpan duration) => Interlocked.Add(ref ticks, duration.Ticks);
}

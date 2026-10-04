namespace ByteBridge.Enrollment;

/*
 * Time and waiting behind one seam, so approval polling that takes half
 * an hour in real life takes no time in a test.
 */
public interface IClock
{
    DateTimeOffset Now { get; }

    Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken);
}

public sealed class SystemClock : IClock
{
    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    public Task DelayAsync(
        TimeSpan duration,
        CancellationToken cancellationToken) =>
        Task.Delay(duration, cancellationToken);
}

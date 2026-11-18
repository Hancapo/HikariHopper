using System.Diagnostics;

namespace HikariHopper.Ui;

/// <summary>
/// The clock the UI's own animations and short timings read: <see cref="Stopwatch"/> time, which only tests stop or
/// move on, together with Avalonia's clocks, so an animation a test is about plays out without waiting for it.
/// </summary>
public static class MotionClock
{
    // Added to Stopwatch time; and the time it stands at while a test holds it, or 0 while it runs.
    private static long offset;
    private static long heldAt;

    /// <summary>Now, as a <see cref="Stopwatch"/> timestamp.</summary>
    public static long Timestamp => heldAt != 0 ? heldAt : Stopwatch.GetTimestamp() + offset;

    /// <summary>The time since <paramref name="timestamp"/>, one of <see cref="Timestamp"/>'s.</summary>
    public static TimeSpan Since(long timestamp) => Stopwatch.GetElapsedTime(timestamp, Timestamp);

    /// <summary>Stops the clock where it is until <see cref="Release"/>; for tests, on the UI thread.</summary>
    internal static void Hold()
    {
        if (heldAt == 0)
            heldAt = Timestamp;
    }

    /// <summary>Moves the clock on by <paramref name="time"/> at once; for tests, on the UI thread.</summary>
    internal static void Skip(TimeSpan time)
    {
        long ticks = (long)(time.TotalSeconds * Stopwatch.Frequency);
        if (heldAt != 0)
            heldAt += ticks;
        else
            offset += ticks;
    }

    /// <summary>Starts the clock again from where it was held.</summary>
    internal static void Release()
    {
        if (heldAt == 0)
            return;
        offset = heldAt - Stopwatch.GetTimestamp();
        heldAt = 0;
    }
}

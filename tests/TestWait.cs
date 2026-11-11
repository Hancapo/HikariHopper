using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Threading;

namespace HikariHopper.Tests;

/// <summary>
/// How UI tests let the app get on with what they asked of it. Waiting a fixed time is for a real timer the test is
/// about (a double click's window, a delay before something shows); anything else waits for what it checks, with
/// <see cref="Until"/>, or for what's queued, with <see cref="Settle"/>.
/// </summary>
internal static class TestWait
{
    /// <summary>
    /// Runs the dispatcher loop (jobs and timers) for about <paramref name="milliseconds"/>, rendering every few
    /// milliseconds. <c>RunJobs</c> alone never promotes due DispatcherTimers.
    /// </summary>
    internal static void Pump(int milliseconds)
    {
        var clock = Stopwatch.StartNew();
        do
        {
            var frame = new DispatcherFrame();
            int slice = (int)Math.Max(1, Math.Min(10, milliseconds - clock.ElapsedMilliseconds));
            _ = Task.Delay(slice).ContinueWith(_ => Dispatcher.UIThread.Post(() => frame.Continue = false, DispatcherPriority.Send));
            Dispatcher.UIThread.PushFrame(frame);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        while (clock.ElapsedMilliseconds < milliseconds);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    /// <summary>Runs what's queued, the timers already due (an animation of no length ends on one) and a render or two.</summary>
    internal static void Settle()
    {
        for (int round = 0; round < 3; round++)
            Pump(1);
    }

    /// <summary>Runs the app until <paramref name="done"/> holds; fails the test when it doesn't within the time given.</summary>
    internal static void Until(Func<bool> done, string what, int timeoutMilliseconds = 5000)
    {
        var clock = Stopwatch.StartNew();
        Settle();
        while (!done())
        {
            Assert.True(clock.ElapsedMilliseconds < timeoutMilliseconds, $"Waited {timeoutMilliseconds} ms for {what}");
            Pump(5);
        }
    }
}

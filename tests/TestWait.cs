using System.Diagnostics;
using Avalonia.Headless;
using Avalonia.Threading;

namespace HikariHopper.Tests;

/// <summary>
/// How UI tests let the app get on with what they asked of it. A test waits for what it checks, with <see cref="Until"/>,
/// or for what's queued, with <see cref="Settle"/>; waiting a set time, with <see cref="Pump"/>, is for a timer or an
/// animation the test is about (a double click's window, a delay before something shows, a fade caught midway), and
/// skips that time on the UI's clocks (<see cref="TestClock"/>) rather than sleeping through it.
/// </summary>
internal static class TestWait
{
    /// <summary>The most time one step skips: what's due runs at least this often.</summary>
    private const int StepMilliseconds = 10;

    /// <summary>
    /// Runs the UI for <paramref name="milliseconds"/> of its time, without waiting for it, then renders. However long
    /// the work takes, the UI sees exactly that much time pass, its frames drawn at the render timer's pace on the way.
    /// <c>RunJobs</c> alone never promotes due DispatcherTimers.
    /// </summary>
    internal static void Pump(int milliseconds)
    {
        Run(milliseconds);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }

    /// <summary>
    /// With the UI's clocks held, moves them on <paramref name="milliseconds"/> in steps of a few, running what is due
    /// after each: jobs, timers (the render timer's among them) and animation frames.
    /// </summary>
    private static void Run(int milliseconds)
    {
        TestClock.Hold();
        try
        {
            int left = Math.Max(1, milliseconds);
            while (left > 0)
            {
                int step = Math.Min(StepMilliseconds, left);
                TestClock.Skip(TimeSpan.FromMilliseconds(step));
                RunDue();
                left -= step;
            }
        }
        finally
        {
            TestClock.Release();
        }
    }

    /// <summary>Runs the dispatcher loop until nothing is left to do now: jobs, and the timers due.</summary>
    private static void RunDue()
    {
        var frame = new DispatcherFrame();
        Dispatcher.UIThread.Post(() => frame.Continue = false, DispatcherPriority.SystemIdle);
        Dispatcher.UIThread.PushFrame(frame);
    }

    /// <summary>Runs what's queued, the timers already due (an animation of no length ends on one) and a render or two.</summary>
    internal static void Settle()
    {
        for (int round = 0; round < 3; round++)
            Pump(1);
    }

    /// <summary>
    /// Runs the app until <paramref name="done"/> holds; fails the test when it doesn't within the time given, of the
    /// UI's time and of real time both, as what it waits for may be work on another thread.
    /// </summary>
    internal static void Until(Func<bool> done, string what, int timeoutMilliseconds = 5000)
    {
        var clock = Stopwatch.StartNew();
        int waited = 0;
        Settle();
        while (!done())
        {
            Assert.True(waited < timeoutMilliseconds || clock.ElapsedMilliseconds < timeoutMilliseconds, $"Waited {timeoutMilliseconds} ms for {what}");
            Run(5);
            waited += 5;
        }
    }
}

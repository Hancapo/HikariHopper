using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using HikariHopper.Ui;

namespace HikariHopper.Tests;

/// <summary>
/// The UI's time, for tests: the app's <see cref="MotionClock"/> and the clocks Avalonia's headless platform keeps,
/// all <see cref="Stopwatch"/>es. Held, they stand still however long the test's work takes, and move on only as far
/// as it skips them, all together; released, they run on in real time from there.
/// </summary>
internal static class TestClock
{
    // Avalonia's clocks: the dispatcher's, for its timers; the media context's, for transitions and animation frames;
    // and each headless window's, for the times of its input (a double click's, a flick's).
    private static readonly Type MediaContext = typeof(Avalonia.Media.Brush).Assembly.GetType("Avalonia.Media.MediaContext", throwOnError: true)!;
    private static readonly PropertyInfo MediaContextInstance = MediaContext.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)
        ?? throw new MissingMemberException(MediaContext.FullName, "Instance");
    private static readonly FieldInfo MediaContextTime = MediaContext.GetField("_time", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(MediaContext.FullName, "_time");
    // Held weakly: a test leaves its windows open when it ends.
    private static readonly List<WeakReference<Window>> Windows = [];
    private static readonly List<Stopwatch> Clocks = [];

    static TestClock()
    {
        Window.WindowOpenedEvent.AddClassHandler(typeof(Window), (sender, _) => Windows.Add(new((Window)sender!)));
        Window.WindowClosedEvent.AddClassHandler(typeof(Window), (sender, _) =>
            Windows.RemoveAll(entry => !entry.TryGetTarget(out Window? window) || window == sender));
    }

    /// <summary>Starts following the windows the tests open; before the first one is.</summary>
    internal static void Begin() => RuntimeHelpers.RunClassConstructor(typeof(TestClock).TypeHandle);

    /// <summary>Stops the UI's clocks where they are, on the UI thread, until <see cref="Release"/>.</summary>
    internal static void Hold()
    {
        Dispatcher.UIThread.VerifyAccess();
        MotionClock.Hold();
        foreach (Stopwatch clock in AvaloniaClocks())
            clock.Stop();
    }

    /// <summary>Moves every clock the UI reads on by <paramref name="time"/>, on the UI thread.</summary>
    internal static void Skip(TimeSpan time)
    {
        Dispatcher.UIThread.VerifyAccess();
        long ticks = (long)(time.TotalSeconds * Stopwatch.Frequency);
        MotionClock.Skip(time);
        foreach (Stopwatch clock in AvaloniaClocks())
            Elapsed(clock) += ticks;
    }

    /// <summary>Lets the UI's clocks run on from where they were held.</summary>
    internal static void Release()
    {
        Dispatcher.UIThread.VerifyAccess();
        foreach (Stopwatch clock in AvaloniaClocks())
            clock.Start();
        MotionClock.Release();
    }

    private static List<Stopwatch> AvaloniaClocks()
    {
        Clocks.Clear();
        if (DispatcherImpl(Dispatcher.UIThread) is not ManagedDispatcherImpl dispatcher)
            throw new InvalidOperationException("The headless dispatcher isn't Avalonia's managed one, whose clock this moves");
        Clocks.Add(DispatcherClock(dispatcher));
        Clocks.Add((Stopwatch)MediaContextTime.GetValue(MediaContextInstance.GetValue(null))!);
        Windows.RemoveAll(entry => !entry.TryGetTarget(out _));
        foreach (WeakReference<Window> entry in Windows)
        {
            if (entry.TryGetTarget(out Window? window) && window.PlatformImpl is { } impl && InputClock(impl.GetType()) is { } clock)
                Clocks.Add((Stopwatch)clock.GetValue(impl)!);
        }
        return Clocks;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_elapsed")]
    private static extern ref long Elapsed(Stopwatch stopwatch);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_impl")]
    private static extern ref IDispatcherImpl DispatcherImpl(Dispatcher dispatcher);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_clock")]
    private static extern ref Stopwatch DispatcherClock(ManagedDispatcherImpl dispatcher);

    private static readonly Dictionary<Type, FieldInfo?> InputClocks = [];

    private static FieldInfo? InputClock(Type impl)
    {
        if (!InputClocks.TryGetValue(impl, out FieldInfo? clock))
            InputClocks[impl] = clock = impl.GetField("_st", BindingFlags.NonPublic | BindingFlags.Instance) is { FieldType: var type } field
                && type == typeof(Stopwatch) ? field : null;
        return clock;
    }
}

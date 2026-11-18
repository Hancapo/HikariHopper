using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using HikariHopper.Ui;

namespace HikariHopper.Tests;

/// <summary>The UI's time in tests: what <see cref="TestWait.Pump"/> skips, every clock the UI reads skips alike.</summary>
public sealed class TestClockTests
{
    [AvaloniaFact]
    public void A_pump_plays_timers_transitions_and_animation_frames_out_on_skipped_time()
    {
        var border = new Border { Width = 100, Height = 100 };
        var window = new Window { Content = border, Width = 200, Height = 200 };
        window.Show();
        TestWait.Settle();
        border.Transitions = [new DoubleTransition { Property = Visual.OpacityProperty, Duration = TimeSpan.FromSeconds(10) }];
        bool fired = false;
        DispatcherTimer.RunOnce(() => fired = true, TimeSpan.FromSeconds(10));
        var frames = new List<TimeSpan>();
        void Frame(TimeSpan time)
        {
            frames.Add(time);
            window.RequestAnimationFrame(Frame);
        }
        window.RequestAnimationFrame(Frame);
        long motion = MotionClock.Timestamp;
        var wall = Stopwatch.StartNew();

        // Halfway through ten seconds, in a fraction of one: the fade is half done, the timer not yet due.
        border.Opacity = 0;
        TestWait.Pump(5000);
        Assert.InRange(border.Opacity, 0.45, 0.55);
        Assert.False(fired);
        Assert.InRange(MotionClock.Since(motion).TotalMilliseconds, 5000, 5000 + wall.Elapsed.TotalMilliseconds);
        // A frame at least every sixtieth of a second, stamped with the time skipped.
        Assert.InRange(frames.Count, 5000 / 17, 5000);
        Assert.InRange((frames[^1] - frames[0]).TotalMilliseconds, 4950, 5000 + wall.Elapsed.TotalMilliseconds);

        TestWait.Pump(5100);
        Assert.Equal(0, border.Opacity);
        Assert.True(fired);
        Assert.True(wall.Elapsed < TimeSpan.FromSeconds(5), $"Ten seconds of the UI's time took {wall.Elapsed} to run");
        window.Close();
    }

    [AvaloniaFact]
    public void While_a_pump_runs_the_UI_sees_only_the_time_it_skips()
    {
        var window = new Window { Width = 200, Height = 200 };
        window.Show();
        TestWait.Settle();
        // Work that takes real time on each frame, more of it than the time skipped: the UI's time doesn't count it.
        var slept = TimeSpan.Zero;
        void Slow(TimeSpan _)
        {
            var nap = Stopwatch.StartNew();
            Thread.Sleep(30);
            slept += nap.Elapsed;
            window.RequestAnimationFrame(Slow);
        }
        window.RequestAnimationFrame(Slow);
        TestWait.Settle();
        long motion = MotionClock.Timestamp;
        slept = TimeSpan.Zero;
        TestWait.Pump(100);
        Assert.True(slept > TimeSpan.FromMilliseconds(100), $"The frames slept {slept}");
        Assert.Equal(100, MotionClock.Since(motion).TotalMilliseconds, 0);
        window.Close();
    }

    [AvaloniaFact]
    public void Input_skipped_apart_is_far_apart()
    {
        var button = new Button { Width = 100, Height = 40 };
        var window = new Window { Content = button, Width = 200, Height = 200 };
        window.Show();
        TestWait.Settle();
        var counts = new List<int>();
        button.AddHandler(InputElement.PointerPressedEvent, (_, e) => counts.Add(e.ClickCount), handledEventsToo: true);
        Point middle = button.TranslatePoint(new Point(50, 20), window)!.Value;
        void Click()
        {
            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle, MouseButton.Left);
        }

        // Two clicks at once make a double click; a second's skip apart, two single ones.
        Click();
        Click();
        TestWait.Pump(1000);
        Click();
        Assert.Equal([1, 2, 1], counts);
        window.Close();
    }
}

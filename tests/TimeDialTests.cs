using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace HikariHopper.Tests;

public sealed class TimeDialTests
{
    [Fact]
    public void The_day_goes_round_clockwise_with_noon_at_the_top()
    {
        Assert.Equal(0, Views.TimeDial.AngleOf(12), 6);
        Assert.Equal(Math.PI / 2, Views.TimeDial.AngleOf(18), 6);
        Assert.Equal(Math.PI, Math.Abs(Views.TimeDial.AngleOf(0)), 6);
        Assert.Equal(-Math.PI / 2, Views.TimeDial.AngleOf(6), 6);
        // Midnight is one place, whichever side of it.
        Assert.Equal(Math.Cos(Views.TimeDial.AngleOf(0)), Math.Cos(Views.TimeDial.AngleOf(24)), 6);
        Assert.Equal(Math.Sin(Views.TimeDial.AngleOf(0)), Math.Sin(Views.TimeDial.AngleOf(24)), 6);
        Assert.Equal(("night", "morning", "afternoon", "evening", "night"),
            (Views.TimeDial.PartOfDay(3), Views.TimeDial.PartOfDay(8), Views.TimeDial.PartOfDay(14.5f), Views.TimeDial.PartOfDay(19), Views.TimeDial.PartOfDay(23)));
        Assert.Equal(("14:30", "00:00", "23:45"), (Views.TimeDial.Clock(14.5f), Views.TimeDial.Clock(24), Views.TimeDial.Clock(23.75f)));
    }

    [AvaloniaFact]
    public void The_handle_is_put_where_the_dial_is_pressed_and_dragged_round_it()
    {
        var dial = new Views.TimeDial { HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 300, Height = 300, Content = dial };
        window.Show();
        Assert.Equal(new Size(152, 152), dial.Bounds.Size);
        var moved = new List<float>();
        dial.Moved += moved.Add;
        Point On(float hours)
        {
            Point handle = dial.HandleAt(hours);
            return dial.TranslatePoint(handle, window)!.Value;
        }
        // At the top, noon; on the right, 18:00; a drag carries on past midnight into the morning.
        Assert.Equal(12f, dial.HoursAt(new Point(76, 10)));
        Assert.Equal(18f, dial.HoursAt(new Point(140, 76)));
        window.MouseDown(On(12), MouseButton.Left);
        Assert.Equal(12f, dial.Hours);
        window.MouseMove(On(18));
        window.MouseMove(On(23.75f));
        window.MouseMove(On(1));
        window.MouseUp(On(1), MouseButton.Left);
        Assert.Equal([12f, 18f, 23.75f, 1f], moved);
        Assert.Equal(1f, dial.Hours);
        // Released, it no longer follows.
        window.MouseMove(On(5));
        Assert.Equal(1f, dial.Hours);
        // The handle sits on the ring where its hour is.
        Assert.Equal(new Point(76, 76 - 152 * .372), dial.HandleAt(12));

        // A quarter hour a step: the wheel and the arrows; an hour with Up and Down; round midnight.
        dial.Hours = 23.75f;
        dial.Step(1);
        Assert.Equal(0f, dial.Hours);
        window.MouseWheel(On(0), new Vector(0, -1));
        Assert.Equal(23.75f, dial.Hours);
        dial.Focus();
        window.KeyPressQwerty(PhysicalKey.ArrowUp, RawInputModifiers.None);
        Assert.Equal(0.75f, dial.Hours);
        window.KeyPressQwerty(PhysicalKey.ArrowLeft, RawInputModifiers.None);
        Assert.Equal(0.5f, dial.Hours);
        // A screen reader reads it as a slider of the day's hours, at the time it is at.
        var peer = (Avalonia.Automation.Provider.IRangeValueProvider)Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(dial);
        Assert.Equal((0, 24, 0.5), (peer.Minimum, peer.Maximum, peer.Value));
        peer.SetValue(6);
        Assert.Equal(6f, dial.Hours);
        // What isn't a time is left alone.
        int before = moved.Count;
        dial.Move(float.NaN);
        dial.Move(float.PositiveInfinity);
        Assert.Equal(6f, dial.Hours);
        Assert.Equal(before, moved.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void The_small_dial_on_the_button_only_shows_the_time()
    {
        var dial = new Views.TimeDial { Compact = true, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 100, Height = 100, Content = dial };
        window.Show();
        Assert.Equal(new Size(15, 15), dial.Bounds.Size);
        int moved = 0;
        dial.Moved += _ => moved++;
        window.MouseDown(dial.TranslatePoint(new Point(7, 1), window)!.Value, MouseButton.Left);
        window.MouseWheel(dial.TranslatePoint(new Point(7, 7), window)!.Value, new Vector(0, 1));
        Assert.Equal(0, moved);
        window.Close();
    }
}

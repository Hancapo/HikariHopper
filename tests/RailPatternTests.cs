using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using HikariHopper.Views;
using Icon = HikariHopper.Views.RailPattern.Icon;

namespace HikariHopper.Tests;

public sealed class RailPatternTests
{
    private static readonly Vector AlongRow = new(Math.Sqrt(.5), -Math.Sqrt(.5));
    private static readonly Vector AcrossRows = new(Math.Sqrt(.5), Math.Sqrt(.5));

    private static double Apart(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static Icon? At(IReadOnlyList<(Icon Kind, Point Centre)> icons, Point point) =>
        icons.Where(icon => Apart(icon.Centre, point) < 0.01).Select(icon => (Icon?)icon.Kind).FirstOrDefault();

    [Fact]
    public void The_icons_run_in_rows_rising_45_degrees_track_engine_signal_in_turn()
    {
        var size = new Size(1360, 800);
        List<(Icon Kind, Point Centre)> icons = [.. RailPattern.Icons(size)];
        // Away from the field's edges, every icon has the next kind one step up its row, and the row below starts half a
        // step on, so no two of a kind sit side by side.
        var inner = icons.Where(icon => icon.Centre.X is > 300 and < 1060 && icon.Centre.Y is > 300 and < 500).ToList();
        Assert.NotEmpty(inner);
        foreach ((Icon kind, Point centre) in inner)
        {
            Assert.Equal((Icon)(((int)kind + 1) % 3), At(icons, centre + AlongRow * RailPattern.Along));
            Assert.Equal((Icon)(((int)kind + 2) % 3), At(icons, centre + AlongRow * (2 * RailPattern.Along)));
            Point below = centre + AcrossRows * RailPattern.Across + AlongRow * (RailPattern.Along / 2);
            Point belowBack = centre + AcrossRows * RailPattern.Across - AlongRow * (RailPattern.Along / 2);
            Assert.True(At(icons, below) is not null || At(icons, belowBack) is not null);
        }
        // Nothing nearer to another icon than a step along a row: the icons keep their margin.
        foreach ((Icon _, Point centre) in inner)
            Assert.DoesNotContain(icons, other => other.Centre != centre && Apart(other.Centre, centre) < RailPattern.Along - 0.01);
    }

    [Fact]
    public void One_repeat_up_its_rows_the_pattern_is_where_it_started()
    {
        // So the drift wraps from a repeat back to none without a jump.
        var size = new Size(1360, 800);
        List<(Icon Kind, Point Centre)> icons = [.. RailPattern.Icons(size)];
        foreach ((Icon kind, Point centre) in icons.Where(icon => icon.Centre.X is > 400 and < 900 && icon.Centre.Y is > 400 and < 700))
            Assert.Equal(kind, At(icons, centre + AlongRow * RailPattern.Repeat));
    }

    [AvaloniaFact]
    public void It_drifts_up_its_rows_while_shown_and_stops_when_it_should()
    {
        Ui.Motion.ReducedOverride = false;
        try
        {
            var pattern = new RailPattern();
            Assert.False(pattern.Animating);
            var window = new Window { Width = 640, Height = 360, Content = pattern };
            window.Show();
            TestWait.Settle();
            Assert.True(pattern.Animating);
            TestWait.Pump(300);
            // Up and to the right, at its slow pace.
            Assert.InRange(pattern.Offset, 0.5, RailPattern.Speed * 1.5);
            var shift = (TranslateTransform)((Control)pattern.GetVisualChildren().Single()).RenderTransform!;
            Assert.True(shift.X > 0 && shift.Y < 0);
            Assert.Equal(shift.X, -shift.Y, 6);

            // While the tab loads a game, under the loading overlay, it stands still; then it goes on from where it was.
            pattern.IsRunning = false;
            Assert.False(pattern.Animating);
            Assert.False(pattern.Watching);
            double stopped = pattern.Offset;
            TestWait.Pump(100);
            Assert.Equal(stopped, pattern.Offset);
            pattern.IsRunning = true;
            TestWait.Pump(100);
            Assert.True(pattern.Offset > stopped && pattern.Offset < stopped + RailPattern.Speed);

            // Animations turned off while it drifts: within half a second it stands still where it is, and waits for them.
            Ui.Motion.ReducedOverride = true;
            TestWait.Until(() => !pattern.Animating, "the drift to stop with motion reduced", 2000);
            Assert.True(pattern.Watching);
            double still = pattern.Offset;
            TestWait.Pump(200);
            Assert.Equal(still, pattern.Offset);
            Ui.Motion.ReducedOverride = false;
            pattern.WatchMotion();
            Assert.True(pattern.Animating);

            // Gone from the window, it asks for nothing.
            window.Content = null;
            Assert.False(pattern.Animating);
            Assert.False(pattern.Watching);
            window.Close();
        }
        finally
        {
            Ui.Motion.ReducedOverride = null;
        }
    }
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using HikariHopper.Views;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Tests;

public sealed class LoadingOverlayTests
{
    private static Window Show(Control content)
    {
        var window = new Window { Width = 640, Height = 360, Content = content, Background = T.AppBg };
        window.Show();
        Pump();
        return window;
    }

    private static void Pump()
    {
        for (int i = 0; i < 3; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
    }

    // The window's pixels, for telling one frame from another.
    private static byte[] Pixels(Window window)
    {
        using WriteableBitmap frame = window.CaptureRenderedFrame()!;
        using ILockedFramebuffer buffer = frame.Lock();
        var bytes = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, bytes, 0, bytes.Length);
        return bytes;
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_train_never_shown_asks_for_nothing(bool reduced)
    {
        Ui.Motion.ReducedOverride = reduced;
        try
        {
            // As every tab's texture editor makes one up front, whose overlay may never show.
            var train = new LoadingTrain();
            Assert.False(train.Animating);
            Assert.False(train.Watching);
            // Past the watcher's first look, too.
            TestWait.Pump(600);
            Assert.False(train.Animating);
            Assert.False(train.Watching);
        }
        finally
        {
            Ui.Motion.ReducedOverride = null;
        }
    }

    [AvaloniaFact]
    public void The_train_moves_only_while_the_overlay_runs_and_motion_is_on()
    {
        Ui.Motion.ReducedOverride = false;
        try
        {
            var overlay = new LoadingOverlay { Title = "LOADING GAME", Detail = "Grand Theft Auto V" };
            Window window = Show(overlay);
            Assert.False(overlay.Train.Animating);
            overlay.IsRunning = true;
            Assert.True(overlay.Train.Animating);
            overlay.IsRunning = false;
            Assert.False(overlay.Train.Animating);
            // With motion reduced it stands still, even while the overlay runs.
            Ui.Motion.ReducedOverride = true;
            overlay.IsRunning = true;
            Assert.False(overlay.Train.Animating);
            Assert.True(overlay.Train.Watching);
            window.Close();
            Assert.False(overlay.Train.Animating);
            Assert.False(overlay.Train.Watching);
        }
        finally
        {
            Ui.Motion.ReducedOverride = null;
        }
    }

    [AvaloniaFact]
    public void The_train_moves_again_when_motion_comes_back_mid_load()
    {
        Ui.Motion.ReducedOverride = false;
        try
        {
            var overlay = new LoadingOverlay { Title = "LOADING GAME", Detail = "Grand Theft Auto V" };
            Window window = Show(overlay);
            overlay.IsRunning = true;
            Assert.True(overlay.Train.Animating);
            Assert.False(overlay.Train.Watching);
            // Animations turned off mid-load: the next frame stills it, and it waits for them to come back.
            Ui.Motion.ReducedOverride = true;
            overlay.Train.InvalidateVisual();
            Pump();
            Assert.False(overlay.Train.Animating);
            Assert.True(overlay.Train.Watching);
            overlay.Train.WatchMotion();
            Assert.False(overlay.Train.Animating);
            // Turned back on: the watcher's next look sets it going and stops looking.
            Ui.Motion.ReducedOverride = false;
            overlay.Train.WatchMotion();
            Assert.True(overlay.Train.Animating);
            Assert.False(overlay.Train.Watching);
            // Once the load ends nothing is left ticking.
            Ui.Motion.ReducedOverride = true;
            overlay.Train.InvalidateVisual();
            Pump();
            overlay.IsRunning = false;
            Assert.False(overlay.Train.Animating);
            Assert.False(overlay.Train.Watching);
            window.Close();
        }
        finally
        {
            Ui.Motion.ReducedOverride = null;
        }
    }

    [AvaloniaFact]
    public void The_train_is_drawn_differently_as_time_passes()
    {
        var overlay = new LoadingOverlay { Title = "LOADING GAME", Detail = "Grand Theft Auto V" };
        Window window = Show(overlay);
        overlay.Train.TimeOverride = .1;
        overlay.Train.InvalidateVisual();
        Pump();
        byte[] early = Pixels(window);
        overlay.Train.TimeOverride = .35;
        overlay.Train.InvalidateVisual();
        Pump();
        Assert.NotEqual(early, Pixels(window));
        // Standing still it shows one moment, smoke in the air: the same each time.
        overlay.Train.TimeOverride = null;
        overlay.Train.InvalidateVisual();
        Pump();
        byte[] still = Pixels(window);
        overlay.Train.TimeOverride = 1.0;
        overlay.Train.InvalidateVisual();
        Pump();
        Assert.Equal(still, Pixels(window));
        window.Close();
    }

    [AvaloniaFact]
    public void The_caption_shows_the_title_over_what_is_loading()
    {
        var overlay = new LoadingOverlay { Title = "LOADING GAME", Detail = "Grand Theft Auto V Enhanced" };
        Window window = Show(overlay);
        TextBlock[] texts = [.. overlay.GetLogicalDescendants().OfType<TextBlock>().Where(text => text.IsEffectivelyVisible)];
        Assert.Equal(["LOADING GAME", "Grand Theft Auto V Enhanced"], texts.Select(text => text.Text));
        Assert.True(texts[0].Bounds.Bottom <= texts[1].Bounds.Top);
        // The train stands over the caption, the whole centred across the window.
        Rect train = overlay.Train.Bounds;
        Assert.Equal(new Size(LoadingTrain.FrameWidth, LoadingTrain.FrameHeight), train.Size);
        Assert.Equal(window.Width / 2, overlay.Train.TranslatePoint(new Point(train.Width / 2, 0), window)!.Value.X, 1);
        Assert.Equal("LOADING GAME Grand Theft Auto V Enhanced", AutomationProperties.GetName(overlay));
        window.Close();
    }

    [AvaloniaFact]
    public void A_long_task_shows_its_bar_and_count_and_can_be_cancelled()
    {
        var overlay = new LoadingOverlay { Title = "IMPORTING TEXTURES", Detail = "adder_paint_d.png" };
        Window window = Show(overlay);
        // Work of no known length has no bar, and nothing to cancel unless it says so.
        Assert.False(overlay.Bar.IsVisible);
        Assert.False(overlay.CancelButton.IsVisible);
        overlay.Progress = .5;
        overlay.ProgressText = "7/12";
        overlay.CanCancel = true;
        Pump();
        Assert.True(overlay.Bar.IsVisible);
        Assert.Equal(new Size(LoadingOverlay.BarWidth, LoadingOverlay.BarHeight), overlay.Bar.Bounds.Size);
        Assert.Equal((LoadingOverlay.BarWidth - 2) / 2, overlay.Fill.Bounds.Width, 3);
        Assert.Contains(overlay.Bar.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == "7/12");
        // The bar sits under the caption, Cancel under the bar.
        Assert.True(overlay.Bar.TranslatePoint(default, window)!.Value.Y > overlay.Train.TranslatePoint(new Point(0, overlay.Train.Bounds.Height), window)!.Value.Y);
        Assert.True(overlay.CancelButton.TranslatePoint(default, window)!.Value.Y > overlay.Bar.TranslatePoint(new Point(0, overlay.Bar.Bounds.Height), window)!.Value.Y);
        int asked = 0;
        overlay.CancelRequested += (_, _) => asked++;
        overlay.CancelButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, asked);
        Assert.False(overlay.CancelButton.IsEnabled);
        // A sliver done still shows both rounded ends; nothing done shows no fill.
        overlay.Progress = .001;
        Assert.Equal(2 * (T.ControlRadius - 1), overlay.Fill.Width);
        overlay.Progress = 0;
        Assert.Equal(0, overlay.Fill.Width);
        window.Close();
    }
}

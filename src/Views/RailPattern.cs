using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;
using HikariHopper.Ui;
using T = HikariHopper.Ui.Theme;

namespace HikariHopper.Views;

/// <summary>
/// The start page's background, and the game loading view's: a piece of track, the loading train's 4-4-0 and a semaphore signal, small and faint,
/// repeated in rows that rise 45° from left to right, each row a step on from the one below. While
/// <see cref="IsRunning"/> the whole field drifts slowly up its rows; with motion reduced it stands still.
/// </summary>
/// <remarks>
/// The icons are drawn once, on a field a repeat larger than the view on every side; drifting moves that field, and
/// after one repeat (three icons along a row) it is where it started, so it wraps without a seam.
/// </remarks>
public sealed class RailPattern : Control
{
    public static readonly StyledProperty<bool> IsRunningProperty =
        AvaloniaProperty.Register<RailPattern, bool>(nameof(IsRunning), true);

    /// <summary>From one icon to the next along a row.</summary>
    internal const double Along = 127;

    /// <summary>From one row to the next, square to them.</summary>
    internal const double Across = 160;

    /// <summary>Along a row, the pattern repeats every three icons: track, engine, signal.</summary>
    internal const double Repeat = 3 * Along;

    /// <summary>How fast the field drifts up its rows, in pixels a second.</summary>
    internal const double Speed = 12;

    // Along a row (up and to the right), and square to it (down and to the right).
    private static readonly Vector AlongRow = new(Math.Sqrt(.5), -Math.Sqrt(.5));
    private static readonly Vector AcrossRows = new(Math.Sqrt(.5), Math.Sqrt(.5));

    internal enum Icon { Track, Engine, Signal }

    private readonly RailField field = new();
    private readonly Stopwatch clock = new();
    private readonly DispatcherTimer ticker;
    // While it should drift with motion reduced, a slow look at the Windows setting, so turning animations back on
    // sets it going again.
    private readonly DispatcherTimer watcher;
    // How far up its rows the field has drifted, shared by every pattern, so one taking over from another (the loading
    // view's from the start page's) is where it was.
    private static double offset;

    public RailPattern()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        VisualChildren.Add(field);
        LogicalChildren.Add(field);
        // Made stopped, and started only while the pattern is shown and running.
        ticker = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        ticker.Tick += (_, _) => Drift();
        watcher = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        watcher.Tick += (_, _) => WatchMotion();
    }

    /// <summary>Whether it drifts: off while the tab loads a game, and behind the loading view, which shows it still.</summary>
    public bool IsRunning
    {
        get => GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    /// <summary>Set by tests to place the field that far up its rows, in pixels.</summary>
    internal double? OffsetOverride { get; set; }

    /// <summary>Whether it is asking for frames.</summary>
    internal bool Animating => ticker.IsEnabled;

    /// <summary>Whether it is waiting for motion to be turned back on.</summary>
    internal bool Watching => watcher.IsEnabled;

    /// <summary>How far up its rows the field is now, within one repeat.</summary>
    internal double Offset => offset;

    /// <summary>Puts a pattern that isn't drifting where the drifting ones have got to.</summary>
    internal void Sync() => Place();

    /// <summary>The watcher's look: once motion is back on, the field drifts again.</summary>
    internal void WatchMotion()
    {
        if (!Motion.Reduced)
            Restart();
    }

    /// <summary>
    /// The icons that cover a field of <paramref name="size"/>, each with where its centre is: rows rising 45° left
    /// to right, each a third of a repeat on from the one before, so the three kinds take turns down the rows too.
    /// </summary>
    internal static IEnumerable<(Icon Kind, Point Centre)> Icons(Size size)
    {
        var middle = new Point(size.Width / 2, size.Height / 2);
        double reach = Math.Sqrt(size.Width * size.Width + size.Height * size.Height) / 2 + Along;
        int rows = (int)Math.Ceiling(reach / Across), steps = (int)Math.Ceiling(reach / Along);
        for (int row = -rows; row <= rows; row++)
        {
            for (int step = -steps; step <= steps; step++)
            {
                Point centre = middle + AlongRow * (step * Along + (row & 1) * Along / 2) + AcrossRows * (row * Across);
                if (centre.X < -Along || centre.Y < -Along || centre.X > size.Width + Along || centre.Y > size.Height + Along)
                    continue;
                yield return ((Icon)(((step + row) % 3 + 3) % 3), centre);
            }
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsRunningProperty)
            Restart();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Restart();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        ticker.Stop();
        watcher.Stop();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        field.Measure(Size.Infinity);
        return default;
    }

    // The field is a repeat larger than the view on every side, so however far it has drifted it covers the view.
    protected override Size ArrangeOverride(Size finalSize)
    {
        field.Arrange(new Rect(-Repeat, -Repeat, finalSize.Width + 2 * Repeat, finalSize.Height + 2 * Repeat));
        Place();
        return finalSize;
    }

    private void Restart()
    {
        bool shown = IsRunning && VisualRoot is not null;
        bool moving = shown && !Motion.Reduced && OffsetOverride is null;
        if (moving && !ticker.IsEnabled)
        {
            // From where it stopped, so starting again doesn't jump.
            clock.Restart();
            motionCheckedAt = 0;
            startedAt = offset;
            ticker.Start();
        }
        else if (!moving)
        {
            ticker.Stop();
        }
        watcher.IsEnabled = shown && !moving && OffsetOverride is null;
        Place();
    }

    private double startedAt;

    private long motionCheckedAt;

    private void Drift()
    {
        // Animations turned off in Windows while it drifts: it stops where it is, and waits for them to come back. Looked
        // at twice a second, as the watcher does, not every frame.
        if (clock.ElapsedMilliseconds - motionCheckedAt >= 500)
        {
            motionCheckedAt = clock.ElapsedMilliseconds;
            if (Motion.Reduced)
            {
                Restart();
                return;
            }
        }
        offset = (startedAt + clock.Elapsed.TotalSeconds * Speed) % Repeat;
        Place();
    }

    // Moves the field, not its drawing: the icons are drawn once.
    private void Place()
    {
        Vector shift = AlongRow * (OffsetOverride ?? offset);
        field.RenderTransform = new TranslateTransform(shift.X, shift.Y);
    }

    /// <summary>The icons, drawn once for the field's size.</summary>
    private sealed class RailField : Control
    {
        private static readonly IImmutableSolidColorBrush Ink = T.RailPatternInk;
        private static readonly ImmutablePen Line = new(Ink, 3, lineCap: PenLineCap.Square, lineJoin: PenLineJoin.Miter);

        // Each icon about 64 by 64 around its centre, one 3 px line with square ends, as the loading train is drawn.
        private static readonly Geometry Track = Geometry.Parse(
            "M-28,-9 H28 M-28,9 H28 M-24,-17 V17 M-12,-17 V17 M0,-17 V17 M12,-17 V17 M24,-17 V17");
        private static readonly Geometry Engine = new GeometryGroup
        {
            Children =
            {
                // Boiler, chimney and its cap, dome, cab with its roof and window, footplate, cowcatcher.
                Geometry.Parse("M-14,6 V-8 H14 V6 M-10,-8 V-18 H-4 V-8 M-12,-20 H-2 M2,-8 V-13 H8 V-8 M14,6 V-20 H28 V6 M12,-22 H30 M18,-15 H24 V-9 H18 Z M-14,6 H30 M-14,-4 L-24,6 H-14 M6,12 H22"),
                // A bogie wheel and two drivers.
                new EllipseGeometry(new Rect(-21, 9, 8, 8)),
                new EllipseGeometry(new Rect(0, 6, 12, 12)),
                new EllipseGeometry(new Rect(16, 6, 12, 12)),
            },
        };
        // Post and foot, the arm raised with its spectacle, the lamp case.
        private static readonly Geometry Signal = Geometry.Parse(
            "M-6,28 V-26 M-16,28 H4 M-6,-22 L22,-32 L24,-26 L-6,-16 M14,-29 V-23 M-18,-10 H-6 V2 H-18 Z");

        public override void Render(DrawingContext context)
        {
            foreach ((Icon kind, Point centre) in Icons(Bounds.Size))
            {
                using (context.PushTransform(Matrix.CreateTranslation(centre.X, centre.Y)))
                    context.DrawGeometry(null, Line, kind switch { Icon.Track => Track, Icon.Engine => Engine, _ => Signal });
            }
        }
    }
}

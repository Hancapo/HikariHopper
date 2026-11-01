using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace HikariHopper.Views;

/// <summary>
/// Smooth mouse-wheel scrolling for a vertical <see cref="ScrollViewer"/>. Each notch moves a target
/// by Windows' wheel-scroll lines × 24px and the view glides after it, once per frame, with an
/// exponential ease-out, so a fast spin builds up speed instead of restarting every notch. Turning
/// the wheel at either end stretches the content a little and lets it spring back.
/// Programmatic scrolls (<see cref="ScrollTo"/>) stop the glide and jump, like <c>positionViewAtIndex</c>.
/// </summary>
public sealed class SmoothScrolling
{
    /// <summary>One notch: the system's wheel-scroll lines (3 by default) × 24px.</summary>
    public static readonly double NotchDistance = 24 * WheelScrollLines();

    // Time constants of the ease-outs: the glide covers 95% of the way in three of them.
    private const double GlideSeconds = 0.07;
    private const double StretchSeconds = 0.045;
    private const double ReleaseSeconds = 0.12;
    // How far the content stretches past an end per notch, and at most.
    private const double StretchPerNotch = 24;
    private const double MaximumStretch = 48;

    private readonly ScrollViewer viewer;
    private readonly DispatcherTimer fallback;
    private readonly Stopwatch clock = new();
    private TimeSpan lastFrame;
    private bool running;
    private double target;
    private double position;
    private double stretch;
    private double stretchTarget;
    private double? pending;

    private SmoothScrolling(ScrollViewer viewer)
    {
        this.viewer = viewer;
        fallback = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Render, (_, _) => Step());
        viewer.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        // A press anywhere in the view, the scrollbar included, takes over from a glide.
        viewer.AddHandler(InputElement.PointerPressedEvent, (_, _) => { if (running) Stop(); }, RoutingStrategies.Tunnel, handledEventsToo: true);
        viewer.ScrollChanged += OnScrollChanged;
    }

    public static SmoothScrolling Attach(ScrollViewer viewer) => new(viewer);

    /// <summary>Whether the view is still gliding or springing back.</summary>
    public bool Animating => running;

    /// <summary>Where the current glide ends.</summary>
    public double Target => running ? target : viewer.Offset.Y;

    private double Maximum => Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);

    /// <summary>Jumps to <paramref name="y"/> (clamped), stopping any glide.</summary>
    public void ScrollTo(double y)
    {
        Stop();
        // A position past the content the view has not laid out yet is applied once it has.
        pending = y > Maximum ? y : null;
        viewer.Offset = viewer.Offset.WithY(Math.Clamp(y, 0, Maximum));
    }

    /// <summary>
    /// ListView.Contain: the smallest move that shows <paramref name="top"/>…<paramref name="bottom"/>
    /// (the top wins when the range is taller than the view).
    /// </summary>
    public void EnsureVisible(double top, double bottom)
    {
        double offset = viewer.Offset.Y;
        double height = viewer.Viewport.Height;
        if (bottom > offset + height)
            offset = bottom - height;
        if (top < offset)
            offset = top;
        if (offset != viewer.Offset.Y)
            ScrollTo(offset);
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.ExtentDelta.Y == 0 && e.ViewportDelta.Y == 0)
        {
            // Something else moved the view: it takes over.
            if (running && Math.Abs(viewer.Offset.Y - position) > 0.5)
                Stop();
            return;
        }
        target = Math.Clamp(target, 0, Maximum);
        if (pending is not { } y)
            return;
        if (y <= Maximum)
            pending = null;
        viewer.Offset = viewer.Offset.WithY(Math.Clamp(y, 0, Maximum));
    }

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0)
            return;
        e.Handled = true;
        pending = null;
        double maximum = Maximum;
        if (!running)
        {
            position = viewer.Offset.Y;
            target = position;
        }
        double wanted = target - e.Delta.Y * NotchDistance;
        target = Math.Clamp(wanted, 0, maximum);
        // Pushing past an end stretches the content that way; it springs back on its own.
        if (wanted < 0 && target <= 0)
            stretchTarget = Math.Min(MaximumStretch, stretchTarget + StretchPerNotch * Math.Abs(e.Delta.Y));
        else if (wanted > maximum && target >= maximum)
            stretchTarget = Math.Max(-MaximumStretch, stretchTarget - StretchPerNotch * Math.Abs(e.Delta.Y));
        Start();
    }

    private void Start()
    {
        if (running)
            return;
        running = true;
        clock.Restart();
        lastFrame = TimeSpan.Zero;
        RequestFrame();
    }

    private void Stop()
    {
        running = false;
        fallback.Stop();
        stretch = stretchTarget = 0;
        ApplyStretch();
    }

    private void RequestFrame()
    {
        if (TopLevel.GetTopLevel(viewer) is { } top)
        {
            fallback.Stop();
            top.RequestAnimationFrame(_ => Step());
        }
        else if (!fallback.IsEnabled)
        {
            fallback.Start();
        }
    }

    private void Step()
    {
        if (!running)
            return;
        TimeSpan now = clock.Elapsed;
        double seconds = Math.Clamp((now - lastFrame).TotalSeconds, 0, 0.05);
        lastFrame = now;

        position += (target - position) * Ease(seconds, GlideSeconds);
        if (Math.Abs(target - position) < 0.5)
            position = target;
        viewer.Offset = viewer.Offset.WithY(Math.Clamp(position, 0, Maximum));

        stretch += (stretchTarget - stretch) * Ease(seconds, StretchSeconds);
        stretchTarget -= stretchTarget * Ease(seconds, ReleaseSeconds);
        if (Math.Abs(stretch) < 0.25 && Math.Abs(stretchTarget) < 0.25)
            stretch = stretchTarget = 0;
        ApplyStretch();

        if (position == target && stretch == 0)
        {
            running = false;
            fallback.Stop();
            return;
        }
        RequestFrame();
    }

    /// <summary>The fraction of the remaining way an exponential ease-out covers in <paramref name="seconds"/>.</summary>
    private static double Ease(double seconds, double timeConstant) => 1 - Math.Exp(-seconds / timeConstant);

    private void ApplyStretch()
    {
        if (viewer.Content is not Visual content)
            return;
        // Fractional on purpose: a stretch of a few pixels moved in whole pixels visibly steps.
        if (stretch == 0)
            content.RenderTransform = null;
        else if (content.RenderTransform is TranslateTransform translate)
            translate.Y = stretch;
        else
            content.RenderTransform = new TranslateTransform(0, stretch);
    }

    private static int WheelScrollLines()
    {
        if (!OperatingSystem.IsWindows())
            return 3;
        uint lines = 3;
        return SystemParametersInfo(0x0068 /* SPI_GETWHEELSCROLLLINES */, 0, ref lines, 0) && lines is > 0 and < 100 ? (int)lines : 3;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref uint value, uint winIni);
}

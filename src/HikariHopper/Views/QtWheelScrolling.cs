using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace HikariHopper.Views;

/// <summary>
/// Qt Quick <c>Flickable</c> mouse-wheel scrolling for a vertical <see cref="ScrollViewer"/>: each
/// notch moves the content <c>wheelScrollLines × 24</c> = 72px from where it currently is, animated
/// with <c>QEasingCurve::OutExpo</c> over 300ms; a wheel turn at the bound it points to is ignored.
/// Programmatic scrolls (<see cref="ScrollTo"/>) stop the animation and jump, like
/// <c>positionViewAtIndex</c>.
/// </summary>
public sealed class QtWheelScrolling
{
    /// <summary>QStyleHints::wheelScrollLines() (3) × 24px.</summary>
    public const double NotchDistance = 72;

    private const double DurationMs = 300;

    private readonly ScrollViewer viewer;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = new();
    private double from;
    private double to;
    private double? pending;

    private QtWheelScrolling(ScrollViewer viewer)
    {
        this.viewer = viewer;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Render, OnTick);
        viewer.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        viewer.ScrollChanged += OnScrollChanged;
    }

    public static QtWheelScrolling Attach(ScrollViewer viewer) => new(viewer);

    /// <summary>Whether a wheel animation is running.</summary>
    public bool Animating => timer.IsEnabled;

    private double Maximum => Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);

    /// <summary>Jumps to <paramref name="y"/> (clamped), stopping any wheel animation.</summary>
    public void ScrollTo(double y)
    {
        timer.Stop();
        // A position past the content the view has not laid out yet is applied once it has.
        pending = y > Maximum ? y : null;
        viewer.Offset = viewer.Offset.WithY(Math.Clamp(y, 0, Maximum));
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (pending is not { } y || (e.ExtentDelta.Y == 0 && e.ViewportDelta.Y == 0))
            return;
        if (y <= Maximum)
            pending = null;
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

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0)
            return;
        e.Handled = true;
        pending = null;
        double current = viewer.Offset.Y;
        double maximum = Maximum;
        if ((e.Delta.Y > 0 && current <= 0) || (e.Delta.Y < 0 && current >= maximum))
            return;
        from = current;
        // The overshoot past the bounds Qt shows before its rebound is clamped here.
        to = Math.Clamp(current - e.Delta.Y * NotchDistance, 0, maximum);
        clock.Restart();
        if (!timer.IsEnabled)
            timer.Start();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / DurationMs);
        double eased = t >= 1 ? 1 : 1.001 * (1 - Math.Pow(2, -10 * t));
        viewer.Offset = viewer.Offset.WithY(Math.Clamp(from + (to - from) * Math.Min(1, eased), 0, Maximum));
        if (t >= 1)
            timer.Stop();
    }
}

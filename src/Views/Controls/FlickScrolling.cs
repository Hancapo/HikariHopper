using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Qt Quick <c>Flickable</c> mouse behaviour for a <see cref="ScrollViewer"/>: dragging with the
/// left button moves the content once past the drag threshold, and releasing while moving flicks
/// it with Qt's default deceleration (1500 px/s², at most 2500 px/s), stopping at the bounds
/// (<c>boundsBehavior: Flickable.StopAtBounds</c>).
/// </summary>
public sealed class FlickScrolling
{
    private const double Deceleration = 1500;
    private const double MaximumVelocity = 2500;
    private const double Threshold = 4;

    private readonly ScrollViewer viewer;
    private readonly DispatcherTimer flickTimer;
    private Point? pressPoint;
    private double pressOffset;
    private bool dragging;
    private double velocity;
    private Point lastPoint;
    private ulong lastTimestamp;
    private DateTime flickTick;

    private FlickScrolling(ScrollViewer viewer)
    {
        this.viewer = viewer;
        flickTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, OnFlickTick);
        viewer.AddHandler(InputElement.PointerPressedEvent, OnPressed, handledEventsToo: false);
        viewer.AddHandler(InputElement.PointerMovedEvent, OnMoved, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerReleasedEvent, OnReleased, handledEventsToo: true);
        viewer.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => flickTimer.Stop(), handledEventsToo: true);
    }

    public static FlickScrolling Attach(ScrollViewer viewer) => new(viewer);

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(viewer).Properties.IsLeftButtonPressed)
            return;
        flickTimer.Stop();
        pressPoint = e.GetPosition(viewer);
        lastPoint = pressPoint.Value;
        lastTimestamp = e.Timestamp;
        pressOffset = viewer.Offset.Y;
        dragging = false;
        velocity = 0;
    }

    private void OnMoved(object? sender, PointerEventArgs e)
    {
        if (pressPoint is not { } origin)
            return;
        Point point = e.GetPosition(viewer);
        if (!dragging && Math.Abs(point.Y - origin.Y) > Threshold)
        {
            dragging = true;
            e.Pointer.Capture(viewer);
        }
        if (!dragging)
            return;
        double elapsed = Math.Max(1, e.Timestamp - lastTimestamp) / 1000.0;
        velocity = Math.Clamp((lastPoint.Y - point.Y) / elapsed, -MaximumVelocity, MaximumVelocity);
        lastPoint = point;
        lastTimestamp = e.Timestamp;
        ScrollTo(pressOffset - (point.Y - origin.Y));
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (pressPoint is null)
            return;
        pressPoint = null;
        if (!dragging)
            return;
        dragging = false;
        e.Pointer.Capture(null);
        if (e.Timestamp - lastTimestamp < 100 && Math.Abs(velocity) > 1)
        {
            flickTick = DateTime.UtcNow;
            flickTimer.Start();
        }
        e.Handled = true;
    }

    private void OnFlickTick(object? sender, EventArgs e)
    {
        DateTime now = DateTime.UtcNow;
        double dt = (now - flickTick).TotalSeconds;
        flickTick = now;
        double step = velocity * dt;
        double slowed = Math.Abs(velocity) - Deceleration * dt;
        velocity = slowed <= 0 ? 0 : Math.Sign(velocity) * slowed;
        double before = viewer.Offset.Y;
        ScrollTo(before + step);
        if (velocity == 0 || viewer.Offset.Y == before)
            flickTimer.Stop();
    }

    private void ScrollTo(double y)
    {
        double maximum = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
        viewer.Offset = viewer.Offset.WithY(Math.Clamp(y, 0, maximum));
    }
}

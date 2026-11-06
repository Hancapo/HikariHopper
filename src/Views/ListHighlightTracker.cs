using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;

namespace HikariHopper.Views;

/// <summary>
/// How a Qt Quick <c>ListView</c> follows its <c>currentIndex</c> when nothing else is configured:
/// it keeps an invisible default highlight on the current row that glides to a new current row with
/// a <c>SmoothedAnimation</c> at <c>highlightMoveVelocity</c> 400 px/s (accelerating, then braking),
/// and the view scrolls only when that highlight and the current row are both past an edge
/// (QQuickListView::trackedPositionChanged). A list without a current row has no highlight, so the
/// next current row is reached at once. The view follows only while the current index moved it
/// (moveReason SetIndex): once the user scrolls, it is theirs until the current index changes again,
/// which matters after a jump across a long list, where the glide goes on for minutes.
/// </summary>
public sealed class ListHighlightTracker
{
    private const double Velocity = 400;

    private readonly ScrollViewer viewer;
    private readonly SmoothScrolling scrolling;
    private readonly double rowHeight;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = new();
    private double? position;
    private double target;
    private int targetIndex = -1;
    private bool following;
    // QSmoothedAnimation state.
    private double start;
    private double velocity;
    private double distance;
    private double accel;
    private double decel;
    private double peakTime;
    private double peakVelocity;
    private double peakDistance;
    private double totalTime;
    private double direction;

    public ListHighlightTracker(ScrollViewer viewer, SmoothScrolling scrolling, double rowHeight)
    {
        this.viewer = viewer;
        this.scrolling = scrolling;
        this.rowHeight = rowHeight;
        timer = new DispatcherTimer(TimeSpan.FromMilliseconds(8), DispatcherPriority.Render, OnTick);
        scrolling.UserScrolled += () => following = false;
    }

    /// <summary>The highlight's top edge in content coordinates, or null without a current row.</summary>
    public double? HighlightPosition => position;

    public bool Animating => timer.IsEnabled;

    /// <summary>The current index changed (QQuickItemView::setCurrentIndex with moveReason SetIndex).</summary>
    public void SetCurrentIndex(int index)
    {
        targetIndex = index;
        following = true;
        if (index < 0)
        {
            timer.Stop();
            position = null;
            return;
        }
        target = index * rowHeight;
        if (position is not { } current)
        {
            position = target;
            timer.Stop();
            Track();
            return;
        }
        double initialVelocity = timer.IsEnabled ? CurrentVelocity() : 0;
        Restart(current, initialVelocity);
        Track();
    }

    /// <summary>Puts the highlight on <paramref name="index"/> at once, without scrolling the view to it.</summary>
    public void Place(int index)
    {
        timer.Stop();
        targetIndex = index;
        if (index < 0)
        {
            position = null;
            return;
        }
        target = index * rowHeight;
        position = target;
    }

    /// <summary>The model was reset: the view regenerates without a current row.</summary>
    public void Reset()
    {
        timer.Stop();
        position = null;
        targetIndex = -1;
    }

    private void Restart(double from, double initialVelocity)
    {
        start = from;
        double s = target - from;
        direction = s < 0 ? -1 : 1;
        distance = Math.Abs(s);
        velocity = initialVelocity * direction;
        if (distance == 0)
        {
            timer.Stop();
            position = target;
            return;
        }
        // QSmoothedAnimation::recalc with maximumEasingTime -1 and no duration.
        double tf = distance / Velocity;
        double c1 = 0.25 * tf * tf;
        double c2 = 0.5 * velocity * tf - distance;
        double c3 = -0.25 * velocity * velocity;
        double a = (-c2 + Math.Sqrt(c2 * c2 - 4 * c1 * c3)) / (2 * c1);
        double tp = 0.5 * tf - 0.5 * velocity / a;
        accel = a;
        decel = a;
        peakTime = tp;
        peakVelocity = a * tp + velocity;
        peakDistance = 0.5 * a * tp * tp + velocity * tp;
        totalTime = tp + peakVelocity / decel;
        clock.Restart();
        if (!timer.IsEnabled)
            timer.Start();
    }

    private double Elapsed => clock.Elapsed.TotalSeconds;

    private double CurrentVelocity()
    {
        double t = Elapsed;
        double v = t < peakTime ? velocity + accel * t : Math.Max(0, peakVelocity - decel * (t - peakTime));
        return v * direction;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        double t = Elapsed;
        double travelled;
        if (t >= totalTime)
        {
            travelled = distance;
        }
        else if (t < peakTime)
        {
            travelled = velocity * t + 0.5 * accel * t * t;
        }
        else
        {
            double dt = t - peakTime;
            travelled = peakDistance + peakVelocity * dt - 0.5 * decel * dt * dt;
        }
        position = start + direction * Math.Min(distance, travelled);
        Track();
        if (t >= totalTime)
        {
            position = target;
            timer.Stop();
        }
    }

    private void Track()
    {
        if (!following || position is not { } tracked || targetIndex < 0)
            return;
        double view = viewer.Offset.Y;
        double size = viewer.Viewport.Height;
        double trackedEnd = tracked + rowHeight;
        double item = target;
        double itemEnd = target + rowHeight;
        double pos = view;
        if (tracked < view && item < view)
        {
            pos = Math.Max(tracked, item);
        }
        else if (trackedEnd >= view + size && itemEnd >= view + size)
        {
            pos = trackedEnd <= itemEnd ? trackedEnd - size : itemEnd - size;
            if (rowHeight > size)
                pos = trackedEnd <= itemEnd ? tracked : item;
        }
        if (pos != view)
            scrolling.ScrollTo(pos);
    }
}

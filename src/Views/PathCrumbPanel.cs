using Avalonia;
using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The address bar's path, in the room it has: every segment when they fit; otherwise the leading ones fold into the
/// overflow mark, its first child, and the current folder and as many before it as fit stay. A current folder too
/// long for the bar on its own is cut short rather than running under the refresh button.
/// </summary>
public sealed class PathCrumbPanel : Panel
{
    private const double Spacing = 2;
    // The least of the current folder that still shows, even next to the overflow mark.
    private const double MinimumLast = 48;

    /// <summary>How many of the leading segments are folded into the overflow mark.</summary>
    public int HiddenCount { get; private set; }

    private Control? Overflow => Children.Count > 0 ? Children[0] : null;

    private int SegmentCount => Math.Max(0, Children.Count - 1);

    private Control Segment(int index) => Children[index + 1];

    protected override Size MeasureOverride(Size availableSize)
    {
        var unbounded = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (Control child in Children)
            child.Measure(unbounded);
        int count = SegmentCount;
        double height = 0;
        double total = 0;
        for (int index = 0; index < count; index++)
        {
            total += Segment(index).DesiredSize.Width + (index > 0 ? Spacing : 0);
            height = Math.Max(height, Segment(index).DesiredSize.Height);
        }
        if (count == 0 || total <= availableSize.Width)
        {
            HiddenCount = 0;
            return new Size(total, height);
        }

        // From the current folder back, as many as fit after the overflow mark.
        double room = availableSize.Width - Overflow!.DesiredSize.Width - Spacing;
        double used = 0;
        int shown = 0;
        for (int index = count - 1; index >= 0; index--)
        {
            double width = Segment(index).DesiredSize.Width + (shown > 0 ? Spacing : 0);
            if (shown > 0 && used + width > room)
                break;
            used += width;
            shown++;
        }
        HiddenCount = count - shown;
        if (used > room)
        {
            // Even the current folder alone is too long: it gets what is left, and its name is cut short.
            Control last = Segment(count - 1);
            last.Measure(new Size(Math.Max(MinimumLast, room), availableSize.Height));
            used = last.DesiredSize.Width;
        }
        return new Size(Overflow.DesiredSize.Width + Spacing + used, Math.Max(height, Overflow.DesiredSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0;
        if (HiddenCount > 0 && Overflow is { } overflow)
        {
            overflow.Arrange(new Rect(0, 0, overflow.DesiredSize.Width, finalSize.Height));
            x = overflow.DesiredSize.Width + Spacing;
        }
        else
        {
            Overflow?.Arrange(default);
        }
        for (int index = 0; index < SegmentCount; index++)
        {
            Control segment = Segment(index);
            if (index < HiddenCount)
            {
                // Folded away: no room, and the row clips, so nothing of it shows or takes clicks.
                segment.Arrange(default);
                continue;
            }
            double width = Math.Min(segment.DesiredSize.Width, Math.Max(0, finalSize.Width - x));
            segment.Arrange(new Rect(x, 0, width, finalSize.Height));
            x += width + Spacing;
        }
        return finalSize;
    }
}

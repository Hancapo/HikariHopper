using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// The shared vertical scrollbar (port of <c>QuietScrollBar.qml</c>): a fixed 12px gutter with a
/// rounded 4px idle thumb that widens to 8px while hovered or pressed. It is always on, like Qt's
/// <c>ScrollBar.AlwaysOn</c>, and draws nothing while the content fits.
/// </summary>
/// <remarks>
/// Use it as <c>PART_VerticalScrollBar</c> of a <see cref="ScrollViewer"/> (see the
/// <c>QuietScrollViewer</c> and <c>QuietOverlayScrollViewer</c> themes) and it binds to the
/// viewer's offset by itself. Pointer handling follows Qt Quick's ScrollBar: pressing outside the
/// thumb grabs it by its centre, dragging moves it, and the press alone does not scroll until the
/// pointer moves or is released. There is no fade: Qt's ScrollBar here has no transitions.
/// </remarks>
public class QuietScrollBar : ScrollBar
{
    public static readonly StyledProperty<string> AccessibleNameProperty =
        AvaloniaProperty.Register<QuietScrollBar, string>(nameof(AccessibleName), "");

    private bool pressed;
    private double grabOffset;

    static QuietScrollBar()
    {
        OrientationProperty.OverrideDefaultValue<QuietScrollBar>(Orientation.Vertical);
        AffectsRender<QuietScrollBar>(ValueProperty, MaximumProperty, ViewportSizeProperty, IsPointerOverProperty);
        FocusableProperty.OverrideDefaultValue<QuietScrollBar>(false);
    }

    public string AccessibleName
    {
        get => GetValue(AccessibleNameProperty);
        set => SetValue(AccessibleNameProperty, value);
    }

    /// <summary>Qt <c>pressed</c>: a press anywhere on the bar until release.</summary>
    public bool IsBarPressed => pressed;

    /// <summary>Qt <c>size</c>: the visible fraction of the content (1 when it all fits).</summary>
    public double VisibleSize
    {
        get
        {
            double extent = Maximum + ViewportSize;
            return extent <= 0 || double.IsNaN(ViewportSize) ? 1 : Math.Clamp(ViewportSize / extent, 0, 1);
        }
    }

    /// <summary>Qt <c>position</c>: the scrolled fraction of the content.</summary>
    public double Position
    {
        get
        {
            double extent = Maximum + ViewportSize;
            return extent <= 0 ? 0 : Math.Clamp(Value / extent, 0, 1);
        }
    }

    /// <summary>Qt binds <c>enabled</c> to <c>size &lt; 1</c>: a bar with nothing to scroll is inert.</summary>
    public bool Scrollable => VisibleSize < 1.0;

    /// <summary>
    /// How far a bar floating over <paramref name="viewer"/>'s right edge reaches into rows that sit
    /// <paramref name="inset"/> inside that edge. 0 while everything fits, when the bar draws nothing.
    /// </summary>
    public static double Overlap(ScrollViewer viewer, double inset) =>
        viewer.Extent.Height > viewer.Viewport.Height ? Math.Max(0, Ui.Theme.ScrollbarWidth - inset) : 0;

    private double AvailableLength => Bounds.Height;

    private double MinimumSize => AvailableLength > 0 ? Math.Min(1, Ui.Theme.ScrollbarMinThumbLength / AvailableLength) : 1;

    /// <summary>The thumb rectangle in local coordinates, snapped the way Qt rasterises an unantialiased rectangle.</summary>
    public Rect ThumbRect
    {
        get
        {
            (double position, double size) = VisualArea();
            double top = position * AvailableLength;
            double bottom = top + size * AvailableLength;
            bool active = IsPointerOver || pressed;
            double width = active ? Ui.Theme.ScrollbarActiveThumbWidth : Ui.Theme.ScrollbarIdleThumbWidth;
            double left = Math.Floor((Bounds.Width - width) / 2 + 0.5);
            double snappedTop = Math.Floor(top + 0.5);
            return new Rect(left, snappedTop, width, Math.Max(0, Math.Floor(bottom + 0.5) - snappedTop));
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AccessibleNameProperty)
            AutomationProperties.SetName(this, AccessibleName);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        base.MeasureOverride(availableSize);
        return new Size(Ui.Theme.ScrollbarWidth, 0);
    }

    public override void Render(DrawingContext context)
    {
        if (!Scrollable)
            return;
        IBrush thumb = pressed ? Ui.Theme.TextRow : IsPointerOver ? Ui.Theme.TextDim : Ui.Theme.Border;
        Rect rect = ThumbRect;
        double radius = rect.Width / 2;
        context.DrawRectangle(thumb, null, rect, radius, radius);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!Scrollable || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        double point = e.GetPosition(this).Y;
        grabOffset = PositionAt(point) - Position;
        double grabbed = Math.Max(VisibleSize, LogicalPosition(MinimumSize));
        if (grabOffset < 0 || grabOffset > grabbed)
            grabOffset = grabbed / 2;
        pressed = true;
        e.Pointer.Capture(this);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (pressed)
            MoveTo(e.GetPosition(this).Y);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!pressed)
            return;
        MoveTo(e.GetPosition(this).Y);
        pressed = false;
        e.Pointer.Capture(null);
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!pressed)
            return;
        pressed = false;
        InvalidateVisual();
    }

    private void MoveTo(double point)
    {
        double position = Math.Max(0.0, Math.Min(PositionAt(point) - grabOffset, 1.0 - VisibleSize));
        SetCurrentValue(ValueProperty, Math.Clamp(position * (Maximum + ViewportSize), Minimum, Maximum));
    }

    private double PositionAt(double point) =>
        AvailableLength <= 0 ? 0 : LogicalPosition(point) / AvailableLength;

    private double LogicalPosition(double position)
    {
        double minimum = MinimumSize;
        double size = VisibleSize;
        return minimum > size && minimum != 1.0 ? position * (1.0 - size) / (1.0 - minimum) : position;
    }

    /// <summary>Port of QQuickScrollBarPrivate::visualArea: the thumb grows to the minimum length.</summary>
    private (double Position, double Size) VisualArea()
    {
        double size = VisibleSize;
        if (size >= 1.0)
            return (0, 1);
        double minimum = MinimumSize;
        double position = Position;
        if (minimum > size)
            position = position / (1.0 - size) * (1.0 - minimum);
        double visualSize = Math.Clamp(Math.Max(size, minimum) + Math.Min(0, position), 0, Math.Max(size, minimum));
        position = Math.Clamp(position, 0, 1.0 - visualSize);
        return (position, visualSize);
    }
}

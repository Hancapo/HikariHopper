using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace HikariHopper.Views;

/// <summary>
/// The world's axes as the camera sees them, as a 3D tool's navigation gizmo draws them over its view: X red, Y green,
/// Z blue, each a labelled ball at the end of its arm, the opposite ends smaller and faded, the nearer drawn over the
/// further. A click on a ball looks at what is framed from that side; on the one already faced, from the other side.
/// A drag on it orbits the camera, as a drag on the view does.
/// </summary>
public sealed class AxesGizmo : Control
{
    internal const double Span = 72;
    private const double Arm = 24, Ball = 7.5, BackBall = 5;
    private const double DragStart = 3;

    internal static readonly IImmutableSolidColorBrush XInk = new ImmutableSolidColorBrush(Color.FromRgb(0xE5, 0x64, 0x6B));
    internal static readonly IImmutableSolidColorBrush YInk = new ImmutableSolidColorBrush(Color.FromRgb(0x8F, 0xCB, 0x6B));
    internal static readonly IImmutableSolidColorBrush ZInk = new ImmutableSolidColorBrush(Color.FromRgb(0x6F, 0xA3, 0xE8));

    private static readonly (Vector3 Axis, string Label, IImmutableSolidColorBrush Ink)[] Axes =
    [
        (Vector3.UnitX, "X", XInk),
        (Vector3.UnitY, "Y", YInk),
        (Vector3.UnitZ, "Z", ZInk),
        (-Vector3.UnitX, "", XInk),
        (-Vector3.UnitY, "", YInk),
        (-Vector3.UnitZ, "", ZInk),
    ];

    private (Vector3 Right, Vector3 Up, Vector3 Forward) basis = (Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY);
    private int hovered = -1;
    private Point? pressedAt;
    private Point lastAt;
    private bool dragged;

    static AxesGizmo()
    {
        AffectsRender<AxesGizmo>(IsPointerOverProperty);
    }

    public AxesGizmo()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>The camera's right, up and forward, in the world, which the axes are drawn in.</summary>
    public (Vector3 Right, Vector3 Up, Vector3 Forward) Basis
    {
        get => basis;
        set
        {
            if (value == basis)
                return;
            basis = value;
            InvalidateVisual();
        }
    }

    /// <summary>Raised when a ball is clicked, with the side to look from: a world axis, or its opposite.</summary>
    public event Action<Vector3>? AxisPicked;

    /// <summary>Raised while the gizmo is dragged, with how far since the last move, in pixels.</summary>
    public event Action<float, float>? Dragged;

    protected override Size MeasureOverride(Size availableSize) => new(Span, Span);

    /// <summary>Where an axis's ball is drawn, and how far it is from the eye (larger is further).</summary>
    internal (Point Centre, float Depth) Place(Vector3 axis)
    {
        var centre = new Point(Span / 2, Span / 2);
        return (new Point(centre.X + Vector3.Dot(axis, basis.Right) * Arm, centre.Y - Vector3.Dot(axis, basis.Up) * Arm),
            Vector3.Dot(axis, basis.Forward));
    }

    /// <summary>The axis whose ball is under a point, the nearest the eye first; -1 for none.</summary>
    internal int AxisAt(Point point)
    {
        int found = -1;
        float nearest = float.MaxValue;
        for (int i = 0; i < Axes.Length; i++)
        {
            (Point centre, float depth) = Place(Axes[i].Axis);
            double radius = (i < 3 ? Ball : BackBall) + 2;
            if ((point - centre).X * (point - centre).X + (point - centre).Y * (point - centre).Y <= radius * radius && depth < nearest)
            {
                found = i;
                nearest = depth;
            }
        }
        return found;
    }

    /// <summary>The side a click on an axis's ball looks from: that axis, or the opposite when it already faces the eye.</summary>
    internal Vector3 SideFor(int index)
    {
        Vector3 axis = Axes[index].Axis;
        return Vector3.Dot(axis, -basis.Forward) > .995f ? -axis : axis;
    }

    /// <summary>Clicks an axis's ball, as the pointer does.</summary>
    internal void Pick(int index)
    {
        if (index >= 0 && index < Axes.Length)
            AxisPicked?.Invoke(SideFor(index));
    }

    public override void Render(DrawingContext context)
    {
        var centre = new Point(Span / 2, Span / 2);
        context.DrawEllipse(new ImmutableSolidColorBrush(Ui.Theme.AppBg.Color, IsPointerOver ? .8 : .55), null, centre, Span / 2 - 2, Span / 2 - 2);
        // Furthest first, so the nearer arms and balls are drawn over them.
        foreach (int i in Enumerable.Range(0, Axes.Length).OrderByDescending(i => Place(Axes[i].Axis).Depth))
        {
            (Vector3 _, string label, IImmutableSolidColorBrush ink) = Axes[i];
            (Point at, float _) = Place(Axes[i].Axis);
            bool front = i < 3;
            IBrush fill = front ? ink : new ImmutableSolidColorBrush(ink.Color, .45);
            context.DrawLine(new Pen(fill, front ? 2.6 : 2, lineCap: PenLineCap.Round), centre, at);
            context.DrawEllipse(fill, i == hovered ? new Pen(Ui.Theme.Text, 1.5) : null, at, front ? Ball : BackBall, front ? Ball : BackBall);
            if (label.Length > 0)
            {
                var text = new FormattedText(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface(Ui.Theme.MonoFont, FontStyle.Normal, FontWeight.Bold), 9, Ui.Theme.BorderHard);
                context.DrawText(text, new Point(at.X - text.Width / 2, at.Y - text.Height / 2));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        pressedAt = lastAt = e.GetPosition(this);
        dragged = false;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        Point at = e.GetPosition(this);
        if (pressedAt is { } from)
        {
            if (!dragged && Math.Abs(at.X - from.X) + Math.Abs(at.Y - from.Y) >= DragStart)
                dragged = true;
            if (dragged)
            {
                Dragged?.Invoke((float)(at.X - lastAt.X), (float)(at.Y - lastAt.Y));
                lastAt = at;
            }
            return;
        }
        int under = AxisAt(at);
        if (under != hovered)
        {
            hovered = under;
            InvalidateVisual();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (pressedAt is null)
            return;
        pressedAt = null;
        e.Pointer.Capture(null);
        if (!dragged)
            Pick(AxisAt(e.GetPosition(this)));
        dragged = false;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        pressedAt = null;
        dragged = false;
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (hovered >= 0)
        {
            hovered = -1;
            InvalidateVisual();
        }
    }
}

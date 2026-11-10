using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using HikariHopper.Rendering;

namespace HikariHopper.Views;

/// <summary>
/// The time of day as a dial: the day is the ring, noon at the top, 18:00 on the right, midnight at the bottom and 06:00
/// on the left, so a day goes round clockwise and midnight is one place. The game's day (sunrise 06:00 to sunset 20:00)
/// is a gold arc, the night a blue one, with a mark where each meets the other. The handle is the sun while the game
/// lights the model with it, the moon otherwise; it is dragged round the ring, a press anywhere puts it there, and the
/// wheel and the arrow keys step a quarter hour (Up and Down an hour). <see cref="Compact"/> draws it small, without
/// labels or the time in its middle, for the button that opens it.
/// </summary>
public sealed class TimeDial : Control
{
    public static readonly StyledProperty<float> HoursProperty =
        AvaloniaProperty.Register<TimeDial, float>(nameof(Hours), DayCycle.DefaultHours);

    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<TimeDial, bool>(nameof(Compact));

    /// <summary>The game's sunrise and sunset, where the sun crosses the horizon.</summary>
    internal const float Sunrise = 6, Sunset = 20;

    internal static readonly IImmutableSolidColorBrush Day = new ImmutableSolidColorBrush(Color.FromRgb(0xF2, 0xC2, 0x61));
    internal static readonly IImmutableSolidColorBrush Night = new ImmutableSolidColorBrush(Color.FromRgb(0x4A, 0x5C, 0x9A));
    private static readonly IImmutableSolidColorBrush Moon = new ImmutableSolidColorBrush(Color.FromRgb(0xD9, 0xDE, 0xF2));

    private bool dragging;

    static TimeDial()
    {
        AffectsRender<TimeDial>(HoursProperty, CompactProperty, IsFocusedProperty);
        FocusableProperty.OverrideDefaultValue<TimeDial>(true);
    }

    /// <summary>The time shown, 0 to 24 hours.</summary>
    public float Hours
    {
        get => GetValue(HoursProperty);
        set => SetValue(HoursProperty, value);
    }

    public bool Compact
    {
        get => GetValue(CompactProperty);
        set => SetValue(CompactProperty, value);
    }

    /// <summary>Raised when the user moves the handle, with the time it is now at, in quarter hours.</summary>
    public event Action<float>? Moved;

    /// <summary>What part of the day an hour is in, under the time in the dial's middle.</summary>
    internal static string PartOfDay(float hours) => Wrap(hours) switch
    {
        < 5 => "night",
        < 12 => "morning",
        < 17 => "afternoon",
        < 21 => "evening",
        _ => "night",
    };

    /// <summary>The angle of an hour on the ring, clockwise from the top (noon), in radians.</summary>
    internal static double AngleOf(float hours) => (Wrap(hours) - 12) / 24.0 * 2 * Math.PI;

    /// <summary>The hour at a point of the dial, to the quarter, from its angle round the centre.</summary>
    internal float HoursAt(Point point)
    {
        Point centre = Centre;
        double angle = Math.Atan2(point.X - centre.X, centre.Y - point.Y);
        double hours = 12 + angle / (2 * Math.PI) * 24;
        return Wrap((float)(Math.Round(hours * 4) / 4));
    }

    /// <summary>The handle's centre for an hour.</summary>
    internal Point HandleAt(float hours)
    {
        double angle = AngleOf(hours);
        return new Point(Centre.X + Radius * Math.Sin(angle), Centre.Y - Radius * Math.Cos(angle));
    }

    private Point Centre => new(Bounds.Width / 2, Bounds.Height / 2);

    // The ring's middle line; its width; the labels sit outside it in the full dial.
    private double Size => Math.Min(Bounds.Width, Bounds.Height);
    private double Ring => Compact ? Size * .16 : Size * .065;
    private double Radius => Compact ? Size / 2 - Ring / 2 : Size * .372;

    protected override Size MeasureOverride(Size availableSize) => Compact ? new Size(15, 15) : new Size(152, 152);

    public override void Render(DrawingContext context)
    {
        double size = Size;
        if (size <= 0)
            return;
        Point centre = Centre;
        // The whole of it takes the pointer, not only what is drawn.
        if (!Compact)
            context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        double radius = Radius, ring = Ring;
        context.DrawEllipse(null, new Pen(Ui.Theme.InsetBg, ring), centre, radius, radius);
        DrawArc(context, Sunrise, Sunset, new ImmutableSolidColorBrush(Day.Color, Compact ? .8 : .6), ring);
        DrawArc(context, Sunset, Sunrise + 24, new ImmutableSolidColorBrush(Night.Color, Compact ? .9 : .7), ring);
        if (!Compact)
        {
            // Marks every three hours across the ring, and the horizon's two, longer, where day and night meet.
            var cut = new Pen(Ui.Theme.AppBg, 1.4);
            for (int hour = 0; hour < 24; hour += 3)
                Radial(context, cut, hour, radius - ring / 2, radius + ring / 2);
            var horizon = new Pen(new ImmutableSolidColorBrush(Ui.Theme.TextFaint.Color, .7), 1, new DashStyle([2, 2], 0));
            Radial(context, horizon, Sunrise, radius - ring * 2.2, radius + ring * 1.4);
            Radial(context, horizon, Sunset, radius - ring * 2.2, radius + ring * 1.4);
            Label(context, "12", 0, -(radius + ring + 7));
            Label(context, "00", 0, radius + ring + 7);
            Label(context, "06", -(radius + ring + 9), 0);
            Label(context, "18", radius + ring + 9, 0);
            FormattedText time = TextViewerInk.Text(Clock(Hours), 17, Ui.Theme.Text);
            context.DrawText(time, new Point(centre.X - time.Width / 2, centre.Y - time.Height / 2 - 6));
            var part = new FormattedText(PartOfDay(Hours), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface(Ui.Theme.UiFont), Ui.Theme.SmallFontSize, Ui.Theme.TextDim);
            context.DrawText(part, new Point(centre.X - part.Width / 2, centre.Y + 9));
        }
        bool sun = DayCycle.Evaluate(Hours).Sun;
        double handle = Compact ? ring * .95 : ring * .9;
        context.DrawEllipse(sun ? Day : Moon, new Pen(Compact ? Ui.Theme.InsetBg : Ui.Theme.AppBg, Compact ? 1 : 2.5),
            HandleAt(Hours), handle, handle);
        if (IsFocused && !Compact)
            context.DrawEllipse(null, new Pen(Ui.Theme.TextDim, 1), HandleAt(Hours), handle + 3, handle + 3);
    }

    // An arc of the ring from one hour round to a later one.
    private void DrawArc(DrawingContext context, float from, float to, IBrush ink, double width)
    {
        double radius = Radius;
        Point centre = Centre;
        double start = AngleOf(from), sweep = (to - from) / 24.0 * 2 * Math.PI;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(new Point(centre.X + radius * Math.Sin(start), centre.Y - radius * Math.Cos(start)), false);
            double end = start + sweep;
            path.ArcTo(new Point(centre.X + radius * Math.Sin(end), centre.Y - radius * Math.Cos(end)), new Size(radius, radius), 0,
                sweep > Math.PI, SweepDirection.Clockwise);
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(ink, width, lineCap: PenLineCap.Flat), geometry);
    }

    // A line across the ring, along the radius at an hour.
    private void Radial(DrawingContext context, IPen pen, float hours, double inner, double outer)
    {
        double angle = AngleOf(hours);
        Point centre = Centre;
        context.DrawLine(pen, new Point(centre.X + inner * Math.Sin(angle), centre.Y - inner * Math.Cos(angle)),
            new Point(centre.X + outer * Math.Sin(angle), centre.Y - outer * Math.Cos(angle)));
    }

    private void Label(DrawingContext context, string text, double dx, double dy)
    {
        FormattedText label = TextViewerInk.Text(text, 9, Ui.Theme.TextFaint);
        context.DrawText(label, new Point(Centre.X + dx - label.Width / 2, Centre.Y + dy - label.Height / 2));
    }

    /// <summary>An hour as the clock says it: 14:30.</summary>
    internal static string Clock(float hours)
    {
        int minutes = (int)Math.Round(hours * 60) % (24 * 60);
        if (minutes < 0)
            minutes += 24 * 60;
        return $"{minutes / 60:00}:{minutes % 60:00}";
    }

    private static float Wrap(float hours)
    {
        float hour = hours % 24;
        return hour < 0 ? hour + 24 : hour;
    }

    // Sets the time from the user, and says so, when it changes.
    private void Move(float hours)
    {
        hours = Wrap(hours);
        if (hours == Hours)
            return;
        Hours = hours;
        Moved?.Invoke(hours);
    }

    /// <summary>Steps the time by a number of quarter hours, round midnight, as the wheel and arrow keys do.</summary>
    internal void Step(int quarters) => Move((float)(Math.Round(Hours * 4) + quarters) / 4);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (Compact || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        Focus(NavigationMethod.Pointer);
        dragging = true;
        e.Pointer.Capture(this);
        Move(HoursAt(e.GetPosition(this)));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragging)
            Move(HoursAt(e.GetPosition(this)));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!dragging)
            return;
        dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        dragging = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (Compact || e.Delta.Y == 0)
            return;
        Step(e.Delta.Y > 0 ? 1 : -1);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        int? quarters = e.Key switch
        {
            Key.Right => 1,
            Key.Left => -1,
            Key.Up => 4,
            Key.Down => -4,
            _ => null,
        };
        if (Compact || quarters is not { } step)
            return;
        Step(step);
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TimeDialPeer(this);

    // To screen readers, a slider of the day's hours.
    private sealed class TimeDialPeer(TimeDial owner) : ControlAutomationPeer(owner), Avalonia.Automation.Provider.IRangeValueProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

        protected override string GetNameCore() => AutomationProperties.GetName(owner) is { Length: > 0 } name ? name : "Time of day";

        public bool IsReadOnly => owner.Compact;
        public double Minimum => 0;
        public double Maximum => 24;
        public double Value => owner.Hours;
        public double SmallChange => .25;
        public double LargeChange => 1;

        public void SetValue(double value) => owner.Move((float)value);
    }
}

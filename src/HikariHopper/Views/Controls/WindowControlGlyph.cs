using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Application-drawn 1px title-bar marks: minimize, maximize, restore and close
/// (port of <c>WindowControlGlyph.qml</c>). The 18x18 mark is centred in the control and steps
/// down by 1px while <see cref="Pressed"/>.
/// </summary>
public class WindowControlGlyph : Control
{
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<WindowControlGlyph, string>(nameof(Kind), "");

    public static readonly StyledProperty<IBrush> InkProperty =
        AvaloniaProperty.Register<WindowControlGlyph, IBrush>(nameof(Ink), Ui.Theme.TextDim);

    public static readonly StyledProperty<bool> PressedProperty =
        AvaloniaProperty.Register<WindowControlGlyph, bool>(nameof(Pressed));

    private const double Box = 18;

    static WindowControlGlyph()
    {
        AffectsRender<WindowControlGlyph>(KindProperty, InkProperty, PressedProperty);
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<WindowControlGlyph>(AccessibilityView.Raw);
    }

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush Ink
    {
        get => GetValue(InkProperty);
        set => SetValue(InkProperty, value);
    }

    public bool Pressed
    {
        get => GetValue(PressedProperty);
        set => SetValue(PressedProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(Box, Box);

    public override void Render(DrawingContext context)
    {
        // anchors.centerIn snaps the 18x18 box to whole pixels (alignWhenCentered).
        double left = QtRound((Bounds.Width - Box) / 2);
        double top = QtRound((Bounds.Height - Box) / 2) + (Pressed ? 1 : 0);
        using (context.PushTransform(Matrix.CreateTranslation(left, top)))
        {
            IBrush ink = Ink;
            switch (Kind)
            {
                case "minimize":
                    context.FillRectangle(ink, new Rect(4, 11, 10, 1));
                    break;
                case "maximize":
                    Outline(context, new Rect(4, 4, 10, 10), null, ink);
                    break;
                case "restore":
                    context.FillRectangle(ink, new Rect(6, 4, 9, 1));
                    context.FillRectangle(ink, new Rect(14, 4, 1, 9));
                    context.FillRectangle(ink, new Rect(6, 4, 1, 3));
                    context.FillRectangle(ink, new Rect(12, 12, 3, 1));
                    Outline(context, new Rect(3, 6, 10, 9), Ui.Theme.ChromeRaised, ink);
                    break;
                case "close":
                    Bar(context, ink, 45);
                    Bar(context, ink, -45);
                    break;
            }
        }
    }

    /// <summary>A QML Rectangle with a 1px border: fill, then the border inside the bounds.</summary>
    private static void Outline(DrawingContext context, Rect rect, IBrush? fill, IBrush ink)
    {
        if (fill is not null)
            context.FillRectangle(fill, rect);
        context.FillRectangle(ink, new Rect(rect.X, rect.Y, rect.Width, 1));
        context.FillRectangle(ink, new Rect(rect.X, rect.Bottom - 1, rect.Width, 1));
        context.FillRectangle(ink, new Rect(rect.X, rect.Y + 1, 1, rect.Height - 2));
        context.FillRectangle(ink, new Rect(rect.Right - 1, rect.Y + 1, 1, rect.Height - 2));
    }

    /// <summary>
    /// An 11x1 rectangle centred in the box and rotated about its own centre, non-antialiased like
    /// Qt's Rectangle. Centring snaps it to (3, 8), so it rotates about (8.5, 8.5).
    /// </summary>
    private static void Bar(DrawingContext context, IBrush ink, double degrees)
    {
        var rotation = Matrix.CreateTranslation(-8.5, -8.5)
            * Matrix.CreateRotation(degrees * Math.PI / 180)
            * Matrix.CreateTranslation(8.5, 8.5);
        using (context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased }))
        using (context.PushTransform(rotation))
            context.FillRectangle(ink, new Rect(3, 8, 11, 1));
    }

    private static double QtRound(double value) => Math.Floor(value + 0.5);
}

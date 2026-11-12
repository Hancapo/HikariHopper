using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace HikariHopper.Views.Controls;

/// <summary>
/// One Lucide mark, drawn as paths (port of <c>LucideIcon.qml</c>).
/// </summary>
/// <remarks>
/// The geometry is vendored in <see cref="LucideIcons"/> on Lucide's 24x24 grid and scaled to
/// <c>min(width, height)</c> from the top-left corner, so an icon is crisp at any size and can be
/// recoloured at runtime. As with Qt's <c>ShapePath.scale</c>, scaling moves the path only: the
/// stroke stays <see cref="Weight"/> pixels wide at every size.
/// </remarks>
public class LucideIcon : Control
{
    /// <summary>The Lucide icon name (QML <c>name</c>; renamed because <c>Name</c> is Avalonia's x:Name).</summary>
    public static readonly StyledProperty<string> IconNameProperty =
        AvaloniaProperty.Register<LucideIcon, string>(nameof(IconName), "");

    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<LucideIcon, IBrush>(nameof(Stroke), Ui.Theme.TextDim);

    // Lucide draws at 2 units on a 24 grid. A hair under that reads better at
    // the sizes this app uses icons at.
    public static readonly StyledProperty<double> WeightProperty =
        AvaloniaProperty.Register<LucideIcon, double>(nameof(Weight), 1.8);

    private static readonly Dictionary<(string Name, double Span), (Geometry, bool)[]> GeometryCache = new();

    static LucideIcon()
    {
        AffectsRender<LucideIcon>(IconNameProperty, StrokeProperty, WeightProperty);
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<LucideIcon>(AccessibilityView.Raw);
    }

    public string IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public IBrush Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Weight
    {
        get => GetValue(WeightProperty);
        set => SetValue(WeightProperty, value);
    }

    /// <summary>The QML implicit size: 16x16.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(16, 16);

    public override void Render(DrawingContext context) =>
        DrawIcon(context, IconName, Stroke, Weight, Math.Min(Bounds.Width, Bounds.Height));

    /// <summary>Strokes the named icon scaled to <paramref name="span"/> pixels from the origin.</summary>
    internal static void DrawIcon(DrawingContext context, string name, IBrush stroke, double weight, double span)
    {
        if (span <= 0 || string.IsNullOrEmpty(name) || !LucideIcons.Paths.ContainsKey(name))
            return;
        var pen = new Pen(stroke, weight, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        IBrush? fill = null;
        foreach ((Geometry geometry, bool filled) in Geometries(name, span))
            if (filled)
                context.DrawGeometry(fill ??= Faded(stroke), null, geometry);
            else
                context.DrawGeometry(null, pen, geometry);
    }

    // A filled part (a shadow) in the stroke's colour, faded so the strokes read over it.
    private static IBrush Faded(IBrush stroke) => stroke is ISolidColorBrush solid
        ? new ImmutableSolidColorBrush(solid.Color, solid.Opacity * .55)
        : stroke;

    private static (Geometry Geometry, bool Filled)[] Geometries(string name, double span)
    {
        if (GeometryCache.TryGetValue((name, span), out (Geometry, bool)[]? cached))
            return cached;
        var transform = new MatrixTransform(Matrix.CreateScale(span / 24, span / 24));
        (Geometry, bool)[] geometries = LucideIcons.Paths[name]
            .Select(data =>
            {
                bool filled = data.StartsWith("fill:", StringComparison.Ordinal);
                Geometry geometry = Geometry.Parse(filled ? data[5..] : data);
                geometry.Transform = transform;
                return (geometry, filled);
            })
            .ToArray();
        if (GeometryCache.Count > 512)
            GeometryCache.Clear();
        GeometryCache[(name, span)] = geometries;
        return geometries;
    }
}

using Avalonia;
using Avalonia.Controls;

namespace HikariHopper.Views.Controls;

/// <summary>
/// A vertical stack with Qt Quick <c>ColumnLayout</c> sizing (for ports of ColumnLayouts):
/// children get their preferred (desired) height plus <see cref="Spacing"/>; extra height goes to
/// children marked <see cref="FillHeightProperty"/> (QML <c>Layout.fillHeight</c>); and when the
/// content does not fit, every child shrinks from its preferred height toward its minimum
/// (<see cref="Layoutable.MinHeight"/> plus vertical margins, Qt's implicit minimum of 0 for plain
/// items) by the same fraction, as QGridLayoutEngine distributes it.
/// </summary>
public class QtColumnLayout : Panel
{
    public static readonly AttachedProperty<bool> FillHeightProperty =
        AvaloniaProperty.RegisterAttached<QtColumnLayout, Control, bool>("FillHeight");

    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<QtColumnLayout, double>(nameof(Spacing));

    static QtColumnLayout()
    {
        AffectsMeasure<QtColumnLayout>(SpacingProperty);
        AffectsParentMeasure<QtColumnLayout>(FillHeightProperty);
    }

    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static bool GetFillHeight(Control control) => control.GetValue(FillHeightProperty);

    public static void SetFillHeight(Control control, bool value) => control.SetValue(FillHeightProperty, value);

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = 0;
        double height = 0;
        int visible = 0;
        foreach (Control child in Children)
        {
            child.Measure(new Size(availableSize.Width, double.PositiveInfinity));
            if (!child.IsVisible)
                continue;
            width = Math.Max(width, child.DesiredSize.Width);
            height += child.DesiredSize.Height;
            visible++;
        }
        return new Size(width, height + Spacing * Math.Max(0, visible - 1));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        List<Control> children = Children.Where(child => child.IsVisible).ToList();
        double spacing = Spacing * Math.Max(0, children.Count - 1);
        double available = finalSize.Height - spacing;
        double[] preferred = children.Select(child => child.DesiredSize.Height).ToArray();
        double[] minimum = children
            .Select(child => Math.Min(child.DesiredSize.Height, child.MinHeight + child.Margin.Top + child.Margin.Bottom))
            .ToArray();
        double sumPreferred = preferred.Sum();
        double sumMinimum = minimum.Sum();
        double[] sizes = (double[])preferred.Clone();
        if (available < sumPreferred)
        {
            double factor = sumPreferred - sumMinimum <= 0 ? 0 : Math.Max(0, (available - sumMinimum) / (sumPreferred - sumMinimum));
            for (int i = 0; i < sizes.Length; i++)
                sizes[i] = minimum[i] + (preferred[i] - minimum[i]) * factor;
        }
        else
        {
            int fillers = children.Count(GetFillHeight);
            if (fillers > 0)
            {
                double extra = (available - sumPreferred) / fillers;
                for (int i = 0; i < sizes.Length; i++)
                {
                    if (GetFillHeight(children[i]))
                        sizes[i] += extra;
                }
            }
        }
        double y = 0;
        for (int i = 0; i < children.Count; i++)
        {
            children[i].Arrange(new Rect(0, y, finalSize.Width, sizes[i]));
            y += sizes[i] + Spacing;
        }
        return finalSize;
    }
}

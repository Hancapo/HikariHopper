using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// A 1px dotted rule, built from discrete dots so it stays pixel-crisp at any length
/// (port of <c>DottedRule.qml</c>). Used for the column separators and the folder-tree guides.
/// </summary>
/// <remarks>
/// The rule is <see cref="DotSize"/> thick across its axis and fills its length along it: a
/// vertical rule measures <c>DotSize x 0</c> and a horizontal one <c>0 x DotSize</c>, so stretch or
/// size it along the axis the way the QML anchored it.
/// </remarks>
public class DottedRule : Control
{
    public static readonly StyledProperty<IBrush> DotColorProperty =
        AvaloniaProperty.Register<DottedRule, IBrush>(nameof(DotColor), Ui.Theme.Guide);

    public static readonly StyledProperty<int> DotSizeProperty =
        AvaloniaProperty.Register<DottedRule, int>(nameof(DotSize), 1);

    public static readonly StyledProperty<int> GapProperty =
        AvaloniaProperty.Register<DottedRule, int>(nameof(Gap), 2);

    public static readonly StyledProperty<bool> HorizontalProperty =
        AvaloniaProperty.Register<DottedRule, bool>(nameof(Horizontal));

    static DottedRule()
    {
        AffectsRender<DottedRule>(DotColorProperty, DotSizeProperty, GapProperty, HorizontalProperty);
        AffectsMeasure<DottedRule>(DotSizeProperty, HorizontalProperty);
        ClipToBoundsProperty.OverrideDefaultValue<DottedRule>(true);
    }

    public IBrush DotColor
    {
        get => GetValue(DotColorProperty);
        set => SetValue(DotColorProperty, value);
    }

    public int DotSize
    {
        get => GetValue(DotSizeProperty);
        set => SetValue(DotSizeProperty, value);
    }

    public int Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public bool Horizontal
    {
        get => GetValue(HorizontalProperty);
        set => SetValue(HorizontalProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) =>
        Horizontal ? new Size(0, DotSize) : new Size(DotSize, 0);

    public override void Render(DrawingContext context)
    {
        int pitch = DotSize + Gap;
        if (pitch <= 0)
            return;
        double length = Horizontal ? Bounds.Width : Bounds.Height;
        int count = Math.Max(0, (int)Math.Ceiling(length / pitch));
        for (int index = 0; index < count; index++)
        {
            double offset = index * pitch;
            var dot = Horizontal ? new Rect(offset, 0, DotSize, DotSize) : new Rect(0, offset, DotSize, DotSize);
            context.FillRectangle(DotColor, dot);
        }
    }
}

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;

namespace HikariHopper.Views.Controls;

/// <summary>
/// Chrome icons by the app's own <c>kind</c> vocabulary, mapped to Lucide (port of <c>ChromeIcon.qml</c>).
/// </summary>
/// <remarks>Add a kind by adding a line to <see cref="Names"/> and vendoring the icon into <see cref="LucideIcons"/>.</remarks>
public class ChromeIcon : Control
{
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<ChromeIcon, string>(nameof(Kind), "");

    public static readonly StyledProperty<IBrush> StrokeProperty =
        AvaloniaProperty.Register<ChromeIcon, IBrush>(nameof(Stroke), Ui.Theme.TextDim);

    public static readonly StyledProperty<double> ThicknessProperty =
        AvaloniaProperty.Register<ChromeIcon, double>(nameof(Thickness), 1.8);

    /// <summary>Kind → Lucide icon name.</summary>
    public static IReadOnlyDictionary<string, string> Names { get; } = new Dictionary<string, string>
    {
        ["back"] = "chevron-left",
        ["forward"] = "chevron-right",
        ["chevron"] = "chevron-right",
        ["caretUp"] = "chevron-up",
        ["caretDown"] = "chevron-down",
        ["up"] = "arrow-up",
        ["refresh"] = "rotate-cw",
        ["list"] = "list",
        ["grid"] = "layout-grid",
        ["checkerboard"] = "grid-3x3",
        ["smooth"] = "filter-smooth",
        ["pixels"] = "filter-pixels",
        ["check"] = "check",
        ["panel"] = "panel-right",
        ["close"] = "x",
        ["plus"] = "plus",
        ["minus"] = "minus",
        ["search"] = "search",
        ["package"] = "package",
    };

    static ChromeIcon()
    {
        AffectsRender<ChromeIcon>(KindProperty, StrokeProperty, ThicknessProperty);
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<ChromeIcon>(AccessibilityView.Raw);
    }

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public IBrush Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public double Thickness
    {
        get => GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }

    /// <summary>The Lucide name <see cref="Kind"/> maps to, or "" for an unknown kind.</summary>
    public string IconName => Names.TryGetValue(Kind, out string? name) ? name : "";

    /// <summary>The QML implicit size: 16x16.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(16, 16);

    public override void Render(DrawingContext context) =>
        LucideIcon.DrawIcon(context, IconName, Stroke, Thickness, Math.Min(Bounds.Width, Bounds.Height));
}

using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Resampling filter picker (port of <c>TextureFilterCombo.qml</c>); <see cref="FlatComboBox.CurrentValue"/> is the
/// filter name. It opens on the filter last applied with, which the tools without the picker use too.
/// </summary>
public class TextureFilterCombo : FlatComboBox
{
    /// <summary>The filter last applied with, for this run of the app: Mitchell until one is.</summary>
    public static string Remembered { get; private set; } = "mitchell";

    /// <summary>Keeps a filter a tool was applied with, for the next dialog and for the tools that don't ask.</summary>
    public static void Remember(string? filter)
    {
        if (!string.IsNullOrEmpty(filter))
            Remembered = filter;
    }

    public TextureFilterCombo()
    {
        ItemsSource = new FlatComboOption[]
        {
            new FlatComboOption("Mitchell filter", "mitchell"),
            new FlatComboOption("Catmull-Rom filter", "catmull-rom"),
            new FlatComboOption("Triangle filter", "triangle"),
            new FlatComboOption("Box filter", "box"),
            new FlatComboOption("Cubic B-spline filter", "cubic-bspline"),
            new FlatComboOption("Point filter", "point"),
        };
        SelectedIndex = Math.Max(0, Array.FindIndex((FlatComboOption[])ItemsSource!, option => Equals(option.Value, Remembered)));
    }

    protected override Type StyleKeyOverride => typeof(FlatComboBox);
}

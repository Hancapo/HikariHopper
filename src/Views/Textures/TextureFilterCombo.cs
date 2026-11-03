using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>Resampling filter picker (port of <c>TextureFilterCombo.qml</c>); <see cref="FlatComboBox.CurrentValue"/> is the filter name.</summary>
public class TextureFilterCombo : FlatComboBox
{
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
        SelectedIndex = 0;
    }

    protected override Type StyleKeyOverride => typeof(FlatComboBox);
}

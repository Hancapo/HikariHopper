using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>Smallest mip level picker (port of <c>TextureMipSizeCombo.qml</c>), 4 × 4 by default; <see cref="FlatComboBox.CurrentValue"/> is the size as an int.</summary>
public class TextureMipSizeCombo : FlatComboBox
{
    public TextureMipSizeCombo()
    {
        ItemsSource = new FlatComboOption[]
        {
            new FlatComboOption("1 × 1", 1),
            new FlatComboOption("2 × 2", 2),
            new FlatComboOption("4 × 4", 4),
            new FlatComboOption("8 × 8", 8),
        };
        SelectedIndex = 2;
    }

    protected override Type StyleKeyOverride => typeof(FlatComboBox);
}

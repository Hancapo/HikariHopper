using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>Compression quality picker (port of <c>TextureQualityCombo.qml</c>), balanced by default; <see cref="FlatComboBox.CurrentValue"/> is the quality as a double.</summary>
public class TextureQualityCombo : FlatComboBox
{
    public TextureQualityCombo()
    {
        ItemsSource = new FlatComboOption[]
        {
            new FlatComboOption("Fast quality", 0.45),
            new FlatComboOption("Balanced quality", 0.7),
            new FlatComboOption("High quality", 1.0),
        };
        SelectedIndex = 1;
    }

    protected override Type StyleKeyOverride => typeof(FlatComboBox);
}

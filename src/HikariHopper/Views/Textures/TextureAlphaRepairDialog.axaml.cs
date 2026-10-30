using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>Repairs dark RGB bleeding around transparent pixels (port of <c>TextureAlphaRepairDialog.qml</c>).</summary>
public partial class TextureAlphaRepairDialog : TextureToolDialog
{
    public TextureAlphaRepairDialog()
    {
        InitializeComponent();
        RadiusCombo.ItemsSource = new FlatComboOption[]
        {
            new("2 pixels", 2),
            new("4 pixels", 4),
            new("8 pixels", 8),
        };
        RadiusCombo.SelectedIndex = 1;
        ApplyAction = () => ViewModel?.RepairSelectedAlphaEdges((int)RadiusCombo.CurrentValue!) == true;
    }
}

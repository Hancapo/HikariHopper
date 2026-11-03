using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>Recalculates the mip chain of the selection (port of <c>TextureMipmapsDialog.qml</c>).</summary>
public partial class TextureMipmapsDialog : TextureToolDialog
{
    public TextureMipmapsDialog()
    {
        InitializeComponent();
        ApplyAction = () => ViewModel?.RecalculateSelectedMipmaps((string)FilterCombo.CurrentValue!, (int)MipSizeCombo.CurrentValue!) == true;
        MipSizeCombo.PropertyChanged += (_, e) =>
        {
            if (e.Property == FlatComboBox.CurrentValueProperty)
                Refresh();
        };
        Refresh();
    }

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        Refresh();
    }

    private void Refresh()
    {
        if (ResultText is null || ViewModel is not { } vm)
            return;
        int minimum = MipSizeCombo.CurrentValue is int value ? value : 0;
        ResultText.Text = vm.SelectedCount > 1
            ? "Recalculate each texture at its original dimensions"
            : $"{vm.MipCount} mips  →  {vm.EstimatedMipCount(vm.SelectedWidth, vm.SelectedHeight, minimum)} mips  ·  last "
              + vm.EstimatedSmallestMipDimensions(vm.SelectedWidth, vm.SelectedHeight, minimum);
    }
}

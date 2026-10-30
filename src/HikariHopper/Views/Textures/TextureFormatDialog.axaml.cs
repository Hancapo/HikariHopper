using System.Text.RegularExpressions;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Converts the selection to another storage format (port of <c>TextureFormatDialog.qml</c>). It
/// opens on the current texture's exact format (<c>"DXT1 (BC1)"</c> selects <c>BC1</c>).
/// </summary>
public partial class TextureFormatDialog : TextureToolDialog
{
    public TextureFormatDialog()
    {
        InitializeComponent();
        ApplyAction = () => ViewModel?.ChangeSelectedFormat((string)FormatCombo.CurrentValue!, (double)QualityCombo.CurrentValue!,
            (string)FilterCombo.CurrentValue!, (int)MipSizeCombo.CurrentValue!) == true;
        FormatCombo.PropertyChanged += (_, e) =>
        {
            if (e.Property == FlatComboBox.DisplayTextProperty)
                Refresh();
        };
    }

    protected override void OnOpened()
    {
        base.OnOpened();
        string exact = Regex.Replace(ViewModel?.SelectedFormat ?? "", @"^.*\((.*)\)$", "$1");
        for (int index = 0; index < FormatCombo.ItemCount; index++)
        {
            if (FormatCombo.Items[index] is FlatComboOption { Value: string value } && value == exact)
            {
                FormatCombo.SelectedIndex = index;
                break;
            }
        }
    }

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        if (property is null && ViewModel is { } vm)
        {
            FormatCombo.ItemsSource = vm.FormatOptions.Select(option => new FlatComboOption(option.Label, option.Value)).ToArray();
            FormatCombo.SelectedIndex = 0;
        }
        Refresh();
    }

    private void Refresh()
    {
        if (ResultText is null || ViewModel is not { } vm)
            return;
        ResultText.Text = vm.SelectedCount > 1
            ? $"All selected textures → {FormatCombo.DisplayText}"
            : $"{vm.SelectedFormat} → {FormatCombo.DisplayText}";
    }
}

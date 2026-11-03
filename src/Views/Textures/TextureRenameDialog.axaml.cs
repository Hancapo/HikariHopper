using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>Renames the single selected texture (port of <c>TextureRenameDialog.qml</c>).</summary>
public partial class TextureRenameDialog : TextureToolDialog
{
    public TextureRenameDialog()
    {
        InitializeComponent();
        ApplyAction = () => ViewModel?.RenameSelected(NameField.Text ?? "") == true;
        NameField.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                ApplyEnabled = (NameField.Text ?? "").Trim().Length > 0;
        };
        ApplyEnabled = false;
    }

    protected override void OnOpened()
    {
        base.OnOpened();
        NameField.Text = ViewModel?.SelectedName ?? "";
        NameField.UpdateLayout();
        NameField.Focus();
        NameField.SelectAll();
    }
}

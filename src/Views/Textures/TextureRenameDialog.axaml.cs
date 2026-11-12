using Avalonia.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Renames the single selected texture. A name it can't take (another texture's, or one with control characters)
/// is said under the field as it is typed, and Rename waits for one it can.
/// </summary>
public partial class TextureRenameDialog : TextureToolDialog
{
    public TextureRenameDialog()
    {
        InitializeComponent();
        ApplyAction = () => ViewModel?.RenameSelected(NameField.Text ?? "") == true;
        NameField.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                Refresh();
        };
        ApplyEnabled = false;
    }

    /// <summary>What is wrong with the name typed, as shown under the field; empty for one it can take, or for none yet.</summary>
    internal string ErrorShown { get; private set; } = "";

    protected override void OnOpened()
    {
        base.OnOpened();
        NameField.Text = ViewModel?.SelectedName ?? "";
        NameField.UpdateLayout();
        Refresh();
        NameField.Focus();
        NameField.SelectAll();
    }

    private void Refresh()
    {
        string name = NameField.Text ?? "";
        string error = ViewModel?.RenameError(name) ?? "";
        ApplyEnabled = error.Length == 0;
        // An empty field has nothing wrong to point at yet: Rename just waits.
        ErrorShown = name.Trim().Length == 0 ? "" : error;
        NameField.Invalid = ErrorShown.Length > 0;
        ErrorHost.Children.Clear();
        if (ErrorShown.Length > 0)
            ErrorHost.Children.Add(TextureOutcome.Warning(ErrorShown, Ui.Theme.Error));
        ErrorHost.IsVisible = ErrorShown.Length > 0;
        FitBody();
    }
}

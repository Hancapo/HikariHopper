using Avalonia.Controls;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Pastes the clipboard image (port of <c>TexturePasteDialog.qml</c>): replace the single selected
/// texture, or add a new texture under a validated name. Cancel or Esc cancels the pending paste;
/// the dialog closes by itself once the view model no longer has one.
/// </summary>
/// <remarks>
/// The replace choice and target name are captured once when the dialog opens, so clipboard
/// notifications do not reset the user's choice. The name field shows the replaced texture's name
/// while replacing and resets to <c>pasted_texture</c> whenever "Add new texture" is chosen, like
/// the QML binding on <c>text</c>.
/// </remarks>
public partial class TexturePasteDialog : TextureToolDialog
{
    private bool replacementAvailable;
    private string replacementName = "";

    public TexturePasteDialog()
    {
        InitializeComponent();
        SetModel();
        ModeCombo.PropertyChanged += (_, e) =>
        {
            if (e.Property == FlatComboBox.CurrentValueProperty)
            {
                NameField.Text = ReplaceMode ? replacementName : "pasted_texture";
                Refresh();
            }
        };
        NameField.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                Refresh();
        };
        ApplyAction = () => ViewModel?.PasteImage(ReplaceMode, NameField.Text ?? "") == true;
        Rejected += (_, _) => ViewModel?.CancelPasteImage();
    }

    public bool ReplaceMode => ModeCombo.CurrentValue as string == "replace";

    /// <summary>QML <c>nameError</c>.</summary>
    public string NameError => ReplaceMode || ViewModel is null ? "" : ViewModel.PasteNameError(NameField.Text ?? "");

    protected override void OnOpened()
    {
        base.OnOpened();
        replacementAvailable = ViewModel?.PasteCanReplace ?? false;
        replacementName = ViewModel?.PasteTargetName ?? "";
        SetModel();
        ModeCombo.SelectedIndex = replacementAvailable ? 0 : 1;
        NameField.Text = ReplaceMode ? replacementName : "pasted_texture";
        if (ReplaceMode)
            ModeCombo.Focus();
        else
        {
            NameField.Focus();
            NameField.SelectAll();
        }
    }

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        Refresh();
        // QML Connections { onStateChanged: if (!pastePending) close() }.
        if (property == nameof(ViewModels.TextureViewerViewModel.PastePending) && ViewModel is { PastePending: false } && IsOpen)
            Close();
    }

    private void SetModel()
    {
        int index = ModeCombo.SelectedIndex;
        ModeCombo.ItemsSource = new FlatComboOption[]
        {
            new("Replace selected texture", "replace", Supported: replacementAvailable),
            new("Add new texture", "add"),
        };
        ModeCombo.SelectedIndex = index < 0 ? 0 : index;
    }

    private void Refresh()
    {
        if (ErrorText is null || ViewModel is not { } vm)
            return;
        string error = NameError;
        NameField.IsEnabled = !ReplaceMode;
        NameField.Invalid = error.Length > 0;
        ErrorText.Text = error;
        ErrorText.IsVisible = error.Length > 0;
        ApplyEnabled = vm.PastePending && (ReplaceMode ? vm.PasteCanReplace : error.Length == 0);
    }
}

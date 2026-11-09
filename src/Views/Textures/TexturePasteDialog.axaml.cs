using Avalonia.Controls;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// Pastes the clipboard image: over the single selected texture, or as a new texture under a name it can take.
/// Cancel or Esc cancels the pending paste; the dialog closes by itself once the view model no longer has one.
/// </summary>
/// <remarks>
/// Whether there is a texture to replace, and its name, are taken once as the dialog opens, so clipboard
/// notifications don't reset the choice. With none to replace there is no choice to show: the image is a new texture.
/// The name starts as <c>pasted_texture</c> each time New texture is picked.
/// </remarks>
public partial class TexturePasteDialog : TextureToolDialog
{
    private bool replacementAvailable;
    private string replacementName = "";

    public TexturePasteDialog()
    {
        InitializeComponent();
        ModeSwitch.Sides.Add(new SwitchSide { Text = "Replace", Width = 74, Label = "Replace the selected texture" });
        ModeSwitch.Sides.Add(new SwitchSide { Text = "New texture", Width = 98, Label = "Add as a new texture" });
        ModeSwitch.SelectedIndex = 1;
        ModeSwitch.SideRequested += (_, side) => SetReplace(side == 0);
        NameField.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
                Refresh();
        };
        ApplyAction = () => ViewModel?.PasteImage(ReplaceMode, ReplaceMode ? replacementName : NameField.Text ?? "") == true;
        Rejected += (_, _) => ViewModel?.CancelPasteImage();
    }

    /// <summary>Whether the image goes over the selected texture, rather than in as a new one.</summary>
    public bool ReplaceMode => replacementAvailable && ModeSwitch.SelectedIndex == 0;

    /// <summary>What is wrong with the new texture's name; empty when it can take it, or when replacing.</summary>
    public string NameError => ReplaceMode || ViewModel is null ? "" : ViewModel.PasteNameError(NameField.Text ?? "");

    /// <summary>The error as shown under the field; empty for none.</summary>
    internal string ErrorShown { get; private set; } = "";

    /// <summary>Picks between replacing the selected texture and adding a new one, as the switch does.</summary>
    internal void SetReplace(bool replace)
    {
        ModeSwitch.SelectedIndex = replace && replacementAvailable ? 0 : 1;
        if (!ReplaceMode)
            NameField.Text = "pasted_texture";
        Refresh();
    }

    protected override void OnOpened()
    {
        base.OnOpened();
        replacementAvailable = ViewModel?.PasteCanReplace ?? false;
        replacementName = ViewModel?.PasteTargetName ?? "";
        SizeText.Text = ViewModel?.PasteImageSize is { } size ? $"{size.Width} × {size.Height}" : "";
        // With a texture selected, replacing it is what a paste over it most likely means.
        SetReplace(replacementAvailable);
        if (ReplaceMode)
            ModeSwitch.Focus();
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

    private void Refresh()
    {
        if (ErrorHost is null || ModeSwitch is null || ViewModel is not { } vm)
            return;
        bool replace = ReplaceMode;
        string error = NameError;
        ModeLabel.IsVisible = ModeSwitch.IsVisible = replacementAvailable;
        NameLabel.Text = replace ? "Replaces" : "Name";
        NameField.IsVisible = !replace;
        TargetText.IsVisible = replace;
        TargetText.Text = replacementName;
        NameField.Invalid = error.Length > 0;
        ErrorShown = error;
        ErrorHost.Children.Clear();
        if (error.Length > 0)
            ErrorHost.Children.Add(TextureOutcome.Warning(error, Ui.Theme.Error));
        ErrorHost.IsVisible = error.Length > 0;
        ApplyEnabled = vm.PastePending && (replace ? vm.PasteCanReplace : error.Length == 0);
        FitBody();
    }
}

namespace HikariHopper.Views;

/// <summary>Confirms removing the selected textures (port of <c>TextureRemoveDialog.qml</c>).</summary>
public partial class TextureRemoveDialog : TextureToolDialog
{
    public TextureRemoveDialog()
    {
        InitializeComponent();
        ApplyAction = () => ViewModel?.RemoveSelected() == true;
        Refresh();
    }

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        Refresh();
    }

    private void Refresh()
    {
        if (QuestionText is null)
            return;
        int count = ViewModel?.SelectedCount ?? 0;
        Heading = count > 1 ? "REMOVE TEXTURES" : "REMOVE TEXTURE";
        ApplyEnabled = ViewModel?.CanRemoveSelection ?? false;
        QuestionText.Text = count > 1 ? $"Remove {count} selected textures?" : "Remove this texture?";
    }
}

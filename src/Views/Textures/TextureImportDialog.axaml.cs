namespace HikariHopper.Views;

/// <summary>
/// Confirms importing dropped images whose names collide with existing textures (port of
/// <c>TextureImportDialog.qml</c>). Cancel or Esc declines; the dialog closes by itself once the
/// view model no longer has a pending import.
/// </summary>
public partial class TextureImportDialog : TextureToolDialog
{
    public TextureImportDialog()
    {
        InitializeComponent();
        ApplyAction = () => ViewModel?.ConfirmImageImport(true) == true;
        Rejected += (_, _) => ViewModel?.ConfirmImageImport(false);
    }

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        if (MessageText is null || ViewModel is not { } vm)
            return;
        ApplyEnabled = vm.ImportConfirmationPending;
        MessageText.Text = $"Import {vm.ImportFileCount} files? {vm.ImportConflictCount} matching names will be replaced "
            + "for the entire batch.\n\nIf files share a name, the last file wins.";
        // QML Connections { onStateChanged: if (!importConfirmationPending) close() }.
        if (property == nameof(vm.ImportConfirmationPending) && !vm.ImportConfirmationPending && IsOpen)
            Close();
    }
}

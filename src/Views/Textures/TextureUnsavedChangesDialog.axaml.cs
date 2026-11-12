namespace HikariHopper.Views;

/// <summary>
/// Asks whether to save the document before it closes or changes (port of
/// <c>TextureUnsavedChangesDialog.qml</c>). Save and Don't save close the dialog and raise
/// <see cref="SaveRequested"/> / <see cref="DiscardRequested"/>; Cancel and Esc reject it.
/// </summary>
public partial class TextureUnsavedChangesDialog : TextureModalDialog
{
    public TextureUnsavedChangesDialog()
    {
        InitializeComponent();
        Height = Ui.Theme.SheetHeaderHeight + 126 + Ui.Theme.DialogFooterHeight;
        CancelButton.Click += (_, _) => Reject();
        CloseMark.Click += (_, _) => Reject();
        DiscardButton.Click += (_, _) =>
        {
            Close();
            DiscardRequested?.Invoke(this, EventArgs.Empty);
        };
        SaveButton.Click += (_, _) =>
        {
            Close();
            SaveRequested?.Invoke(this, EventArgs.Empty);
        };
    }

    /// <summary>The document asked about, when it isn't the texture viewer's: a 3D viewer's file, say.</summary>
    public string? DocumentName
    {
        get => documentName;
        set
        {
            documentName = value;
            OnViewModelChanged(null);
        }
    }

    private string? documentName;

    public event EventHandler? SaveRequested;

    public event EventHandler? DiscardRequested;

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        if (QuestionText is null)
            return;
        QuestionText.Text = $"Save changes to {documentName ?? ViewModel?.SourceName ?? ""}?";
        SaveButton.IsEnabled = ViewModel is not { OperationBusy: true };
    }
}

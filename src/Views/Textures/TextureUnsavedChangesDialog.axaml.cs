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
        Height = Ui.Theme.HeaderHeight + 126 + 58;
        CancelButton.Click += (_, _) => Reject();
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

    public event EventHandler? SaveRequested;

    public event EventHandler? DiscardRequested;

    protected override void OnViewModelChanged(string? property)
    {
        base.OnViewModelChanged(property);
        if (QuestionText is null)
            return;
        QuestionText.Text = $"Save changes to {ViewModel?.SourceName ?? ""}?";
        SaveButton.IsEnabled = ViewModel is not { OperationBusy: true };
    }
}

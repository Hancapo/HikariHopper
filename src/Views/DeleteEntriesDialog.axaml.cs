using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Confirms deleting the selection (port of <c>DeleteEntriesDialog.qml</c>). <see cref="Open"/> shows it;
/// it closes itself once the deletion starts or is cancelled.
/// </summary>
public partial class DeleteEntriesDialog : UserControl
{
    private readonly ExplorerViewModel bridge;
    private ModalOverlay? overlay;

    public DeleteEntriesDialog() : this(new ExplorerViewModel())
    {
    }

    public DeleteEntriesDialog(ExplorerViewModel bridge)
    {
        this.bridge = bridge;
        InitializeComponent();
        Height = Ui.Theme.SheetHeaderHeight + 64 + Ui.Theme.DialogFooterHeight;
        Refresh();
    }

    /// <summary>QML <c>closed</c>.</summary>
    public event EventHandler? Closed;

    /// <summary>The question for <c>selectionCount</c> entries.</summary>
    public string Question => bridge.SelectionCount == 1
        ? $"Are you sure you want to delete “{bridge.SelectedName}”?"
        : $"Are you sure you want to delete {bridge.SelectionCount} items?";

    public void Open(Control anchor)
    {
        bridge.PropertyChanged += OnBridgePropertyChanged;
        overlay = ModalOverlay.Show(anchor, this);
        if (overlay is null)
            return;
        overlay.Closed += (_, _) =>
        {
            bridge.PropertyChanged -= OnBridgePropertyChanged;
            Closed?.Invoke(this, EventArgs.Empty);
        };
    }

    public void Close() => overlay?.Close();

    /// <summary>QML <c>submit()</c>.</summary>
    public void Submit()
    {
        if (bridge.DeleteSelectedFiles())
            Close();
    }

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    private void Refresh()
    {
        QuestionText.Text = Question;
        ConfirmButton.IsEnabled = !bridge.EntryOperationBusy;
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnDelete(object? sender, RoutedEventArgs e) => Submit();
}

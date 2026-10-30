using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Accepts files dragged in from outside and imports them into the current location (port of
/// <c>ExternalFileDropArea.qml</c>): drags with URLs are accepted unless they carry the explorer's
/// own entry-drag format, while the tab has a workspace and no entry operation runs.
/// </summary>
public partial class ExternalFileDropArea : UserControl
{
    private Control? area;
    private bool acceptableDrag;
    private bool containsDrag;

    public ExternalFileDropArea() => InitializeComponent();

    private ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    /// <summary>DropArea.enabled.</summary>
    public bool DropEnabled => Bridge is { HasWorkspace: true, EntryOperationBusy: false };

    /// <summary>Whether the highlight is showing (<c>containsDrag &amp;&amp; acceptableDrag</c>).</summary>
    public bool HighlightVisible => Highlight.IsVisible;

    /// <summary>Takes the drag events of <paramref name="target"/>, the area this overlay covers.</summary>
    public void Attach(Control target)
    {
        area = target;
        DragDrop.SetAllowDrop(target, true);
        target.AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        target.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        target.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        target.AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary><c>accepts(drag)</c>.</summary>
    public bool Accepts(IDataTransfer data) =>
        data.Contains(DataFormat.File) && !data.Contains(EntryPointerArea.EntryDragFormat) && DropEnabled;

    private void Refresh() => Highlight.IsVisible = containsDrag && acceptableDrag;

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (!containsDrag)
        {
            containsDrag = true;
            acceptableDrag = Accepts(e.DataTransfer);
            Refresh();
        }
        e.DragEffects = acceptableDrag ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = acceptableDrag ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        // Leaving one child for another inside the area is not leaving the DropArea.
        if (area is not null && new Rect(area.Bounds.Size).Contains(e.GetPosition(area)))
            return;
        containsDrag = false;
        acceptableDrag = false;
        Refresh();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        bool accepted = Accepts(e.DataTransfer);
        containsDrag = false;
        acceptableDrag = false;
        Refresh();
        e.Handled = true;
        if (!accepted || Bridge is not { } bridge)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        IStorageItem[] files = e.DataTransfer.TryGetFiles() ?? [];
        var paths = files.Select(file => file.TryGetLocalPath()).ToList();
        e.DragEffects = bridge.ImportDroppedFiles(paths) ? DragDropEffects.Copy : DragDropEffects.None;
    }
}

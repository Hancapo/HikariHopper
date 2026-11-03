using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Port of <c>EntryPointerArea.qml</c>, the per-entry MouseArea of the table rows and grid cells:
/// a press selects an unselected entry at once (a click on a selected one re-selects on release, so
/// a multi-selection survives the start of a drag), moving 10px with the left button starts a
/// drag of the selection, a double-click activates, and a right press selects the entry if needed
/// and asks for the context menu at the press point. The owning view routes its pointer events here.
/// </summary>
public sealed class EntryPointerArea
{
    /// <summary>QStyleHints::startDragDistance() on Windows.</summary>
    public const double StartDragDistance = 10;

    private readonly Func<ExplorerViewModel?> bridge;
    private readonly Func<Control> focusTarget;
    private readonly Action<Control, Point> contextMenuRequested;
    private PointerPressedEventArgs? pressEvent;
    private Point pressPoint;
    private int entryIndex = -1;
    private bool dragStarted;
    private bool selectionChangedOnPress;
    private bool doubleClicked;

    public EntryPointerArea(Func<ExplorerViewModel?> bridge, Func<Control> focusTarget, Action<Control, Point> contextMenuRequested)
    {
        this.bridge = bridge;
        this.focusTarget = focusTarget;
        this.contextMenuRequested = contextMenuRequested;
    }

    /// <summary>Whether a left press on an entry is being tracked.</summary>
    public bool Pressed => pressEvent is not null;

    /// <summary>The entry under the tracked press.</summary>
    public int EntryIndex => entryIndex;

    /// <summary>onPressed (and onDoubleClicked for the second press of a double-click).</summary>
    public void OnPressed(PointerPressedEventArgs e, int index, bool selected, Control source, Point point)
    {
        ExplorerViewModel? model = bridge();
        if (model is null)
            return;
        PointerPointProperties properties = e.GetCurrentPoint(source).Properties;
        if (properties.IsRightButtonPressed)
        {
            if (!selected)
                model.SelectEntry(index, e.KeyModifiers);
            focusTarget().Focus(NavigationMethod.Pointer);
            contextMenuRequested(source, point);
            return;
        }
        if (!properties.IsLeftButtonPressed)
            return;
        pressEvent = e;
        entryIndex = index;
        pressPoint = point;
        dragStarted = false;
        selectionChangedOnPress = !selected;
        if (selectionChangedOnPress)
            model.SelectEntry(index, e.KeyModifiers);
        focusTarget().Focus(NavigationMethod.Pointer);
        // Avalonia keeps counting while presses stay close in time and place; as on Windows,
        // every second press of the sequence is a double-click.
        doubleClicked = e.ClickCount % 2 == 0;
        if (doubleClicked && !dragStarted)
            model.ActivateEntry(index);
    }

    /// <summary>onPositionChanged: past the drag distance the selection is dragged out as copies.</summary>
    public void OnMoved(PointerEventArgs e, Point point)
    {
        if (pressEvent is not { } press || dragStarted)
            return;
        double dx = point.X - pressPoint.X;
        double dy = point.Y - pressPoint.Y;
        if (dx * dx + dy * dy < StartDragDistance * StartDragDistance)
            return;
        dragStarted = true;
        _ = StartDragAsync(press, entryIndex);
    }

    /// <summary>onClicked: a click on an entry that was already selected selects it on release.</summary>
    public void OnReleased(PointerReleasedEventArgs e, bool inside)
    {
        if (pressEvent is null)
            return;
        pressEvent = null;
        if (e.InitialPressMouseButton == MouseButton.Left && inside && !doubleClicked && !dragStarted && !selectionChangedOnPress)
            bridge()?.SelectEntry(entryIndex, e.KeyModifiers);
        doubleClicked = false;
    }

    /// <summary>onCanceled.</summary>
    public void OnCanceled()
    {
        pressEvent = null;
        dragStarted = false;
        doubleClicked = false;
    }

    /// <summary>
    /// <c>bridge.startEntryDrag(row)</c>: the view model exports the selection and the view starts the
    /// native drag (Python's <c>QDrag.exec(CopyAction)</c>) with the files plus the internal marker
    /// format, so the explorer's own drop area refuses it.
    /// </summary>
    private async Task StartDragAsync(PointerPressedEventArgs press, int row)
    {
        ExplorerViewModel? model = bridge();
        if (model is null)
            return;
        IReadOnlyList<string> paths = model.StartEntryDrag(row);
        if (paths.Count == 0 || TopLevel.GetTopLevel(focusTarget()) is not { } top)
            return;
        if (DragWithBadgeCursor(press, top, paths))
        {
            OnCanceled();
            return;
        }
        var data = new DataTransfer();
        foreach (string path in paths)
        {
            IStorageItem? item = Directory.Exists(path)
                ? await top.StorageProvider.TryGetFolderFromPathAsync(path)
                : await top.StorageProvider.TryGetFileFromPathAsync(path);
            if (item is not null)
                data.Add(DataTransferItem.CreateFile(item));
        }
        data.Add(DataTransferItem.Create(EntryDragFormat, [(byte)'1']));
        try
        {
            await DragDrop.DoDragDropAsync(press, data, DragDropEffects.Copy);
        }
        catch (Exception error) when (error is InvalidOperationException or NotSupportedException)
        {
            // No platform drag source (headless); Qt would simply not start the drag.
        }
        OnCanceled();
    }

    /// <summary>
    /// Windows: drags the files with the badge as the cursor (<see cref="DragCountBadge"/>). False where that isn't
    /// possible (headless, another platform, items without a shared parent folder), leaving the plain drag to the caller.
    /// </summary>
    private static bool DragWithBadgeCursor(PointerPressedEventArgs press, TopLevel top, IReadOnlyList<string> paths)
    {
        if (!OperatingSystem.IsWindows() || Application.Current?.ApplicationLifetime is null)
            return false;
        int folders = paths.Count(Directory.Exists);
        int files = paths.Count - folders;
        double scale = top.RenderScaling;
        nint Cursor(bool blocked)
        {
            var (pixels, width, height) = DragCountBadge.Render(DragCountBadge.CursorVisual(folders, files, blocked), scale);
            return Infrastructure.ShellFileDrag.CreateCursor(pixels, width, height, 0, 0);
        }
        nint cursor = Cursor(blocked: false), blockedCursor = Cursor(blocked: true);
        try
        {
            // Windows' drag loop takes the mouse from here; the press must not stay captured.
            press.Pointer.Capture(null);
            return Infrastructure.ShellFileDrag.TryDrag(paths, ExplorerViewModel.EntryDragMime, cursor, blockedCursor);
        }
        finally
        {
            Infrastructure.ShellFileDrag.DestroyCursor(cursor);
            Infrastructure.ShellFileDrag.DestroyCursor(blockedCursor);
        }
    }

    /// <summary>The internal format (the MIME type registered as a clipboard format) that marks drags started by the explorer itself.</summary>
    public static DataFormat<byte[]> EntryDragFormat { get; } =
        DataFormat.CreateBytesPlatformFormat(ExplorerViewModel.EntryDragMime);
}

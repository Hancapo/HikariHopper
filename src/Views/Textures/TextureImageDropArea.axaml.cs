using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// Accepts image files dropped on the texture window (port of <c>TextureImageDropArea.qml</c>).
/// </summary>
/// <remarks>
/// A drag of file URLs is accepted (copy) when <see cref="TextureViewerViewModel.CanAcceptImageDrop"/>
/// agrees, and then shows the selection-wash overlay with "DROP IMAGES TO IMPORT"; dropping hands
/// the paths to <see cref="TextureViewerViewModel.ImportDroppedImages"/>. Files that aren't on disk (an image
/// dragged out of a browser or a zip) are copied into a temporary folder off the UI thread first, and imported
/// from there once the copy is done; no drop is taken meanwhile.
/// Nothing is accepted while <see cref="DropEnabled"/> is false. <see cref="Body"/> is the content the area covers.
/// </remarks>
public partial class TextureImageDropArea : UserControl
{
    public static readonly StyledProperty<bool> DropEnabledProperty =
        AvaloniaProperty.Register<TextureImageDropArea, bool>(nameof(DropEnabled));

    public static readonly StyledProperty<Control?> BodyProperty =
        AvaloniaProperty.Register<TextureImageDropArea, Control?>(nameof(Body));

    private bool containsDrag;
    private bool acceptableDrag;
    private DragStaging? staging;
    private bool copying;

    public TextureImageDropArea()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        AddHandler(DragDrop.DropEvent, OnDrop);
    }

    /// <summary>QML <c>enabled</c>: the window's <c>canImportImages</c> with no dialog open.</summary>
    public bool DropEnabled
    {
        get => GetValue(DropEnabledProperty);
        set => SetValue(DropEnabledProperty, value);
    }

    public Control? Body
    {
        get => GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    /// <summary>Whether the overlay is showing (QML <c>containsDrag &amp;&amp; acceptableDrag</c>).</summary>
    public bool OverlayVisible => Overlay.IsVisible;

    /// <summary>The copy of the last drop's files that aren't on disk, while or after it runs.</summary>
    internal Task<string[]>? StagingCopy { get; private set; }

    private bool Accepting => DropEnabled && !copying;

    private TextureViewerViewModel? ViewModel => DataContext as TextureViewerViewModel;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BodyProperty)
            Host.Content = Body;
    }

    /// <summary>The dropped items, or null without any.</summary>
    private static IStorageItem[]? DroppedItems(DragEventArgs e) => e.DataTransfer.TryGetFiles() is { Length: > 0 } items ? items : null;

    /// <summary>
    /// The paths the drop would import: local paths as they are, and for files without one (whose contents
    /// <see cref="Stage"/> copies), a path under <paramref name="folder"/> with the file's name. Items that are
    /// neither keep their URI, which the view model refuses.
    /// </summary>
    private static string[] PathsOf(IStorageItem[] items, string folder) =>
        [.. items.Select((item, index) => item.TryGetLocalPath()
            ?? (item is IStorageFile ? Path.Combine(folder, index.ToString(), StagedName(item.Name)) : item.Path.ToString()))];

    private static string StagedName(string name)
    {
        string file = Path.GetFileName(name.Replace('/', '\\'));
        foreach (char invalid in Path.GetInvalidFileNameChars())
            file = file.Replace(invalid, '_');
        return file.Length > 0 ? file : "image";
    }

    /// <summary>
    /// Copies the files that aren't on disk to <paramref name="paths"/>, then disposes every item: the source of
    /// a virtual file keeps its data for us until it is disposed. Runs off the UI thread.
    /// </summary>
    private static async Task<string[]> CopyAsync(IStorageItem[] items, string[] paths)
    {
        try
        {
            for (int index = 0; index < items.Length; index++)
            {
                if (items[index].TryGetLocalPath() is not null || items[index] is not IStorageFile file)
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(paths[index])!);
                await using Stream source = await file.OpenReadAsync().ConfigureAwait(false);
                await using FileStream target = File.Create(paths[index]);
                await source.CopyToAsync(target).ConfigureAwait(false);
            }
            return paths;
        }
        finally
        {
            Dispose(items);
        }
    }

    private static void Dispose(IStorageItem[] items)
    {
        foreach (IStorageItem item in items)
            item.Dispose();
    }

    /// <summary>Copies the files that aren't on disk into a new temporary folder, then imports the drop.</summary>
    private async void StageAndImport(IStorageItem[] items, TextureViewerViewModel viewModel)
    {
        copying = true;
        string[] paths;
        try
        {
            staging = new DragStaging();
            string[] staged = PathsOf(items, staging.Name);
            Task<string[]> copy = Task.Run(() => CopyAsync(items, staged));
            StagingCopy = copy;
            paths = await copy;
        }
        catch (Exception exception)
        {
            // Whatever the source or the disk throws, nothing was imported; an async void must not let it escape.
            viewModel.ReportImageImportFailure(exception.Message);
            Dispose(items);
            return;
        }
        finally
        {
            copying = false;
        }
        viewModel.ImportDroppedImages(paths);
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (containsDrag)
        {
            // Moving between children of the area raises DragEnter again; the drag never left.
            OnDragOver(sender, e);
            return;
        }
        if (!Accepting || DroppedItems(e) is not { } items)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        // Only the names matter here; the drop reads the files again.
        acceptableDrag = ViewModel?.CanAcceptImageDrop(PathsOf(items, Path.GetTempPath())) ?? false;
        Dispose(items);
        containsDrag = acceptableDrag;
        e.DragEffects = acceptableDrag ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        Refresh();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = containsDrag && acceptableDrag && Accepting ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object? sender, DragEventArgs e)
    {
        // DragLeave also bubbles from children the pointer moves between; only leaving the area counts.
        if (IsPointerInside(e))
            return;
        containsDrag = false;
        acceptableDrag = false;
        Refresh();
    }

    private void OnDrop(object? sender, DragEventArgs e)
    {
        bool wasContained = containsDrag;
        containsDrag = false;
        acceptableDrag = false;
        Refresh();
        e.Handled = true;
        e.DragEffects = DragDropEffects.None;
        if (!wasContained || !Accepting || DroppedItems(e) is not { } items)
            return;
        // The last drop's copies were imported (or refused) before this drop could be taken.
        staging?.Cleanup();
        staging = null;
        if (ViewModel is not { } viewModel || !viewModel.CanAcceptImageDrop(PathsOf(items, Path.GetTempPath())))
        {
            Dispose(items);
            return;
        }
        if (items.All(item => item.TryGetLocalPath() is not null))
        {
            if (viewModel.ImportDroppedImages(PathsOf(items, "")))
                e.DragEffects = DragDropEffects.Copy;
            Dispose(items);
            return;
        }
        e.DragEffects = DragDropEffects.Copy;
        StageAndImport(items, viewModel);
    }

    private bool IsPointerInside(DragEventArgs e) => new Rect(Bounds.Size).Contains(e.GetPosition(this));

    private void Refresh() => Overlay.IsVisible = containsDrag && acceptableDrag;
}

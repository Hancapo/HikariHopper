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
/// dragged out of a browser or a zip) are copied into a temporary folder first and imported from there.
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

    /// <summary>Copies the dropped files that aren't on disk into a new temporary folder (replacing the last one)
    /// and returns the paths to import.</summary>
    private string[] Stage(IStorageItem[] items)
    {
        if (items.All(item => item.TryGetLocalPath() is not null))
            return PathsOf(items, "");
        staging?.Cleanup();
        staging = new DragStaging();
        string[] paths = PathsOf(items, staging.Name);
        for (int index = 0; index < items.Length; index++)
        {
            if (items[index].TryGetLocalPath() is not null || items[index] is not IStorageFile file)
                continue;
            Directory.CreateDirectory(Path.GetDirectoryName(paths[index])!);
            // The drag's data is only readable until the drop returns, so the copy happens now.
            using Stream source = file.OpenReadAsync().GetAwaiter().GetResult();
            using FileStream target = File.Create(paths[index]);
            source.CopyTo(target);
        }
        return paths;
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (containsDrag)
        {
            // Moving between children of the area raises DragEnter again; the drag never left.
            OnDragOver(sender, e);
            return;
        }
        if (!DropEnabled || DroppedItems(e) is not { } items)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        acceptableDrag = ViewModel?.CanAcceptImageDrop(PathsOf(items, Path.GetTempPath())) ?? false;
        containsDrag = acceptableDrag;
        e.DragEffects = acceptableDrag ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        Refresh();
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = containsDrag && acceptableDrag && DropEnabled ? DragDropEffects.Copy : DragDropEffects.None;
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
        if (!wasContained || !DropEnabled || DroppedItems(e) is not { } items || ViewModel is not { } viewModel)
            return;
        string[] paths;
        try
        {
            paths = Stage(items);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            viewModel.ReportImageImportFailure(exception.Message);
            return;
        }
        if (viewModel.ImportDroppedImages(paths))
            e.DragEffects = DragDropEffects.Copy;
    }

    private bool IsPointerInside(DragEventArgs e) => new Rect(Bounds.Size).Contains(e.GetPosition(this));

    private void Refresh() => Overlay.IsVisible = containsDrag && acceptableDrag;
}

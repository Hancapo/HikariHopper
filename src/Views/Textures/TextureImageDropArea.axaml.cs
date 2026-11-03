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
/// the paths to <see cref="TextureViewerViewModel.ImportDroppedImages"/>. Nothing is accepted while
/// <see cref="DropEnabled"/> is false. <see cref="Body"/> is the content the area covers.
/// </remarks>
public partial class TextureImageDropArea : UserControl
{
    public static readonly StyledProperty<bool> DropEnabledProperty =
        AvaloniaProperty.Register<TextureImageDropArea, bool>(nameof(DropEnabled));

    public static readonly StyledProperty<Control?> BodyProperty =
        AvaloniaProperty.Register<TextureImageDropArea, Control?>(nameof(Body));

    private bool containsDrag;
    private bool acceptableDrag;

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

    /// <summary>The dropped items as local paths (or URIs for items without one), or null without file URLs.</summary>
    private static IReadOnlyList<string>? DroppedPaths(DragEventArgs e)
    {
        IStorageItem[]? items = e.DataTransfer.TryGetFiles();
        if (items is null || items.Length == 0)
            return null;
        return [.. items.Select(item => item.TryGetLocalPath() ?? item.Path.ToString())];
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (containsDrag)
        {
            // Moving between children of the area raises DragEnter again; the drag never left.
            OnDragOver(sender, e);
            return;
        }
        if (!DropEnabled || DroppedPaths(e) is not { } paths)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        acceptableDrag = ViewModel?.CanAcceptImageDrop(paths) ?? false;
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
        if (wasContained && DropEnabled && DroppedPaths(e) is { } paths && ViewModel?.ImportDroppedImages(paths) == true)
            e.DragEffects = DragDropEffects.Copy;
        else
            e.DragEffects = DragDropEffects.None;
    }

    private bool IsPointerInside(DragEventArgs e) => new Rect(Bounds.Size).Contains(e.GetPosition(this));

    private void Refresh() => Overlay.IsVisible = containsDrag && acceptableDrag;
}

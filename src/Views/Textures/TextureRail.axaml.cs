using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The texture list on the left of the texture window (port of <c>TextureRail.qml</c>).
/// </summary>
/// <remarks>
/// A left click selects (Ctrl toggles, Shift extends) and focuses the list; a right press makes the
/// row current without dropping a selection that contains it, and the click opens
/// <see cref="TextureContextMenu"/> at the pointer, as does a right click on empty space. Up/Down
/// (with modifiers), Home/End, Ctrl+A, the Menu key and Shift+F10 work while the list has focus.
/// The current row is scrolled into view whenever it changes. Commands that need a dialog are
/// raised as events for <see cref="TextureDictionaryWindow"/>.
/// </remarks>
public partial class TextureRail : UserControl
{
    private readonly TextureContextMenu contextMenu = new();
    private TextureViewerViewModel? viewModel;
    private int currentIndex = -1;

    public TextureRail()
    {
        InitializeComponent();
        FlickScrolling.Attach(Scroller);
        SmoothScrolling.Attach(Scroller);
        Scroller.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<QuietScrollBar>("PART_VerticalScrollBar") is { } bar)
                bar.AccessibleName = "Texture list scroll bar";
        };
        Scroller.PropertyChanged += (_, e) =>
        {
            if (e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.ViewportProperty)
                UpdateScrollable();
        };
        List.AddHandler(PointerPressedEvent, RowPressed);
        List.AddHandler(PointerReleasedEvent, RowReleased);
        List.KeyDown += ListKeyDown;
        AddHandler(PointerReleasedEvent, EmptyReleased);
        contextMenu.ResizeRequested += (_, _) => ResizeRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.PowerOfTwoRequested += (_, _) => PowerOfTwoRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.MipmapsRequested += (_, _) => MipmapsRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.FormatRequested += (_, _) => FormatRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.AlphaRepairRequested += (_, _) => AlphaRepairRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.RenameRequested += (_, _) => RenameRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.RemoveRequested += (_, _) => RemoveRequested?.Invoke(this, EventArgs.Empty);
    }

    public event EventHandler? ResizeRequested;

    public event EventHandler? PowerOfTwoRequested;

    public event EventHandler? MipmapsRequested;

    public event EventHandler? FormatRequested;

    public event EventHandler? AlphaRepairRequested;

    public event EventHandler? RenameRequested;

    public event EventHandler? RemoveRequested;

    /// <summary>The rail's context menu (QML <c>textureContextMenu</c>).</summary>
    public TextureContextMenu ContextMenuPopup => contextMenu;

    /// <summary>The focusable texture list (QML <c>textureSelectionList</c>).</summary>
    public ItemsControl SelectionList => List;

    /// <summary>Whether the rows give up the scrollbar's gutter (QML <c>textureScrollBar.enabled</c>).</summary>
    public bool Scrollable { get; private set; }

    /// <summary>Opens the context menu with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>) in
    /// <paramref name="relativeTo"/>'s coordinates.</summary>
    public void OpenContextMenu(Visual relativeTo, double x, double y)
    {
        Point point = relativeTo.TranslatePoint(new Point(x, y), this) ?? new Point(x, y);
        RetroMenuPlacement.Open(contextMenu, this, point);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (viewModel is not null)
            viewModel.PropertyChanged -= ViewModelChanged;
        viewModel = DataContext as TextureViewerViewModel;
        contextMenu.DataContext = viewModel;
        if (viewModel is not null)
            viewModel.PropertyChanged += ViewModelChanged;
        currentIndex = viewModel?.SelectedIndex ?? -1;
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(TextureViewerViewModel.SelectedIndex) || viewModel is null)
            return;
        if (viewModel.SelectedIndex == currentIndex)
            return;
        currentIndex = viewModel.SelectedIndex;
        if (currentIndex >= 0 && currentIndex < viewModel.TextureCount)
            List.ScrollIntoView(currentIndex);
    }

    private void UpdateScrollable()
    {
        bool scrollable = Scroller.Extent.Height > Scroller.Viewport.Height;
        if (scrollable == Scrollable)
            return;
        Scrollable = scrollable;
        List.Margin = new Thickness(0, 0, scrollable ? Ui.Theme.ScrollbarWidth : 0, 0);
        List.Classes.Set("scrollable", scrollable);
    }

    private (Control Row, int Index)? RowAt(object? source)
    {
        if (viewModel is null || source is not Visual visual)
            return null;
        foreach (Visual ancestor in visual.GetSelfAndVisualAncestors())
        {
            if (ancestor == List)
                return null;
            if (ancestor is Control { DataContext: TextureItem item } control && control.Classes.Contains("row"))
            {
                int index = viewModel.TexturesModel.IndexOf(item);
                return index >= 0 ? (control, index) : null;
            }
        }
        return null;
    }

    private void RowPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(List).Properties.IsRightButtonPressed && RowAt(e.Source) is { } row)
            viewModel?.SelectContextTexture(row.Index);
    }

    private void RowReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (RowAt(e.Source) is not { } row || viewModel is null)
            return;
        // QML onClicked: released over the row that took the press.
        Point position = e.GetPosition(row.Row);
        if (!new Rect(row.Row.Bounds.Size).Contains(position))
            return;
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            viewModel.SelectTexture(row.Index, e.KeyModifiers);
            List.Focus(NavigationMethod.Pointer);
            e.Handled = true;
        }
        else if (e.InitialPressMouseButton == MouseButton.Right)
        {
            List.Focus(NavigationMethod.Pointer);
            OpenContextMenu(row.Row, position.X, position.Y);
            e.Handled = true;
        }
    }

    private void EmptyReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Handled || e.InitialPressMouseButton != MouseButton.Right)
            return;
        Point position = e.GetPosition(this);
        if (!new Rect(Bounds.Size).Contains(position))
            return;
        OpenContextMenu(this, position.X, position.Y);
        e.Handled = true;
    }

    private void ListKeyDown(object? sender, KeyEventArgs e)
    {
        if (viewModel is null)
            return;
        int selected = viewModel.SelectedIndex;
        int count = viewModel.TextureCount;
        switch (e.Key)
        {
            case Key.Up:
                if (selected > 0)
                    viewModel.SelectTexture(selected - 1, e.KeyModifiers);
                e.Handled = true;
                break;
            case Key.Down:
                if (selected + 1 < count)
                    viewModel.SelectTexture(selected + 1, e.KeyModifiers);
                e.Handled = true;
                break;
            case Key.A when e.KeyModifiers.HasFlag(KeyModifiers.Control):
                viewModel.SelectAllTextures();
                e.Handled = true;
                break;
            case Key.Home when count > 0:
                viewModel.SelectTexture(0, e.KeyModifiers);
                e.Handled = true;
                break;
            case Key.End when count > 0:
                viewModel.SelectTexture(count - 1, e.KeyModifiers);
                e.Handled = true;
                break;
            case Key.Apps:
            case Key.F10 when e.KeyModifiers.HasFlag(KeyModifiers.Shift):
                if (List.ContainerFromIndex(selected) is Control container && container.IsVisible)
                {
                    OpenContextMenu(container, 76, container.Bounds.Height / 2);
                    e.Handled = true;
                }
                break;
        }
        // Qt's ListView has key navigation off and its Flickable ignores keys: nothing else scrolls the list.
        if (e.Key is Key.PageUp or Key.PageDown or Key.Home or Key.End)
            e.Handled = true;
    }
}

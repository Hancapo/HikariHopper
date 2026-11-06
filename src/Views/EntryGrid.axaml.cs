using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using HikariHopper.ViewModels;

namespace HikariHopper.Views;

/// <summary>
/// The icon field (port of <c>EntryGrid.qml</c>); its DataContext is the active tab's view model.
/// GridView geometry: <see cref="Ui.Theme.GridCellWidth"/>×<see cref="Ui.Theme.GridCellHeight"/> cells
/// after a <see cref="Ui.Theme.GridContentInset"/> margin, as many columns as fit.
/// </summary>
public partial class EntryGrid : UserControl, IEntryItemsView
{
    private const double Inset = Ui.Theme.GridContentInset;
    private const double CellWidth = Ui.Theme.GridCellWidth;
    private const double CellHeight = Ui.Theme.GridCellHeight;

    private readonly SmoothScrolling wheel;
    private readonly EntryPointerArea pointerArea;
    private readonly ItemCollection<int> rows = [];
    private ExplorerViewModel? bridge;
    private INotifyCollectionChanged? entries;
    private int lastCount = -1;
    private int columns = 1;

    private Point origin;
    private Point pointer;
    private bool tracking;
    private bool marqueeActive;
    private KeyModifiers pressModifiers;
    private bool cellGesture;

    public EntryGrid()
    {
        InitializeComponent();
        wheel = SmoothScrolling.Attach(Scroller);
        pointerArea = new EntryPointerArea(() => Bridge, () => RowList, RequestContextMenu);
        RowList.Owner = this;
        RowList.ItemsSource = rows;
        GridArea.AddHandler(PointerPressedEvent, OnPointerPressed, handledEventsToo: false);
        GridArea.AddHandler(PointerMovedEvent, OnPointerMoved, handledEventsToo: false);
        GridArea.AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: false);
        GridArea.AddHandler(PointerCaptureLostEvent, OnCaptureLost, handledEventsToo: true);
        RowList.KeyDown += OnGridKeyDown;
        GridArea.SizeChanged += (_, _) => RebuildRows(force: false);
    }

    /// <summary>QML <c>contextMenuRequested(source, x, y)</c>, with the point in <c>source</c>'s coordinates.</summary>
    public event EventHandler<ContextMenuRequest>? ContextMenuRequested;

    internal ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    /// <summary>The GridView's width (the panel minus the scrollbar gutter).</summary>
    internal double ViewWidth => Math.Max(0, GridArea.Bounds.Width - Ui.Theme.ScrollbarWidth);

    /// <summary><c>columnsVisible</c>.</summary>
    public int ColumnsVisible => Math.Max(1, (int)Math.Floor((ViewWidth - 2 * Inset) / CellWidth));

    internal int Columns => columns;

    internal bool MarqueeActive => marqueeActive;

    internal ScrollViewer ScrollViewer => Scroller;

    /// <summary>Qt's contentX/contentY: the content origin sits after the margins.</summary>
    private double ContentX => -Inset;

    private double ContentY => Scroller.Offset.Y - Inset;

    private int Count => Bridge?.EntriesModel.Count ?? 0;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (bridge is not null)
            bridge.PropertyChanged -= OnBridgePropertyChanged;
        if (entries is not null)
            entries.CollectionChanged -= OnEntriesChanged;
        bridge = Bridge;
        entries = bridge?.EntriesModel;
        if (bridge is not null)
            bridge.PropertyChanged += OnBridgePropertyChanged;
        if (entries is not null)
            entries.CollectionChanged += OnEntriesChanged;
        lastCount = Count;
        RebuildRows(force: true);
        // Created with its model: the count changes from 0, so positionViewAtBeginning().
        wheel.ScrollTo(Count > 0 ? Inset : 0);
    }

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerViewModel.SearchQuery))
        {
            foreach (EntryGridCell cell in RealizedCells())
                cell.Refresh();
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        int count = Count;
        RebuildRows(force: true);
        // A reset regenerates the view at its start (the top margin showing, contentY -topMargin);
        // onCountChanged: positionViewAtBeginning() puts the first row at the top edge (contentY 0).
        if (e.Action == NotifyCollectionChangedAction.Reset)
            wheel.ScrollTo(0);
        if (count != lastCount)
            wheel.ScrollTo(Inset);
        lastCount = count;
    }

    private void RebuildRows(bool force)
    {
        int newColumns = ColumnsVisible;
        int rowCount = (int)Math.Ceiling(Count / (double)newColumns);
        if (!force && newColumns == columns && rowCount == rows.Count)
            return;
        columns = newColumns;
        rows.ResetItems(Enumerable.Range(0, rowCount));
        EmptyText.IsVisible = Count == 0;
    }

    ScrollViewer IEntryItemsView.Viewer => Scroller;

    Control IEntryItemsView.Header => Header;

    void IEntryItemsView.ShowFirst(int index) => wheel.ScrollTo(Inset + index / ColumnsVisible * CellHeight);

    IEnumerable<(int Index, Control Item, Control Glyph, IReadOnlyList<Control> Details)> IEntryItemsView.ShownItems() =>
        RealizedCells().Where(cell => cell.Item is not null && cell.Index >= 0)
            .Select(cell => (cell.Index, (Control)cell, (Control)cell.Glyph, (IReadOnlyList<Control>)[cell.NameText, cell.SizeText]));

    internal IEnumerable<EntryGridCell> RealizedCells()
    {
        foreach (Control container in RowList.GetRealizedContainers())
        {
            if (container is not EntryGridRow row)
                continue;
            foreach (EntryGridCell cell in row.Cells)
                yield return cell;
        }
    }

    internal EntryGridCell? CellAt(int index)
    {
        if (index < 0 || RowList.ContainerFromIndex(index / columns) is not EntryGridRow row)
            return null;
        return row.CellAt(index % columns);
    }

    /// <summary><c>entryGrid.indexAt(x + contentX, y + contentY)</c>.</summary>
    internal int IndexAtPoint(double x, double y)
    {
        double contentX = x + ContentX;
        double contentY = y + ContentY;
        if (contentX < 0 || contentY < 0)
            return -1;
        int column = (int)Math.Floor(contentX / CellWidth);
        int row = (int)Math.Floor(contentY / CellHeight);
        if (column >= columns)
            return -1;
        int index = row * columns + column;
        return index < Count ? index : -1;
    }

    /// <summary>positionViewAtIndex(index, GridView.Contain).</summary>
    private void PositionViewAtIndex(int index)
    {
        int row = index / columns;
        wheel.EnsureVisible(Inset + row * CellHeight, Inset + (row + 1) * CellHeight);
    }

    private void RequestContextMenu(Control source, Point point) =>
        ContextMenuRequested?.Invoke(this, new ContextMenuRequest(source, point));

    private void UpdateSelection()
    {
        if (Bridge is not { } model)
            return;
        double left = Math.Min(origin.X, pointer.X) + ContentX;
        double right = Math.Max(origin.X, pointer.X) + ContentX;
        double top = Math.Min(origin.Y, pointer.Y) + ContentY;
        double bottom = Math.Max(origin.Y, pointer.Y) + ContentY;
        model.UpdateMarqueeRows(GridMath.IndicesInRect(Count, ColumnsVisible, CellWidth, CellHeight, Inset, Inset,
            left, right, top, bottom));
    }

    private void ShowMarquee()
    {
        Marquee.IsVisible = marqueeActive;
        if (marqueeActive)
            Marquee.Place(origin, pointer);
    }

    private Point Clamp(Point point) =>
        new(GridMath.Clamp(point.X, 0, ViewWidth), GridMath.Clamp(point.Y, 0, GridArea.Bounds.Height));

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Point point = e.GetPosition(GridArea);
        if (point.X >= ViewWidth || Bridge is not { } model)
            return;
        PointerPointProperties properties = e.GetCurrentPoint(GridArea).Properties;
        int index = IndexAtPoint(point.X, point.Y);
        if (index >= 0)
        {
            if (CellAt(index) is not { } cell)
                return;
            cellGesture = properties.IsLeftButtonPressed;
            pointerArea.OnPressed(e, index, model.EntriesModel[index].Selected, cell, e.GetPosition(cell));
            if (cellGesture)
                e.Pointer.Capture(GridArea);
            e.Handled = true;
            return;
        }
        if (!model.HasWorkspace)
            return;
        if (properties.IsRightButtonPressed)
        {
            tracking = false;
            marqueeActive = false;
            ShowMarquee();
            model.ClearEntrySelection();
            RowList.Focus(NavigationMethod.Pointer);
            RequestContextMenu(MarqueeLayer, point);
            e.Handled = true;
            return;
        }
        if (!properties.IsLeftButtonPressed)
            return;
        origin = Clamp(point);
        pointer = origin;
        pressModifiers = e.KeyModifiers;
        tracking = true;
        marqueeActive = false;
        ShowMarquee();
        RowList.Focus(NavigationMethod.Pointer);
        e.Pointer.Capture(GridArea);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (cellGesture)
        {
            if (pointerArea.Pressed && CellAt(pointerArea.EntryIndex) is { } cell)
                pointerArea.OnMoved(e, e.GetPosition(cell));
            return;
        }
        if (!tracking || Bridge is not { } model)
            return;
        pointer = Clamp(e.GetPosition(GridArea));
        double dx = pointer.X - origin.X;
        double dy = pointer.Y - origin.Y;
        double threshold = EntryPointerArea.StartDragDistance;
        if (!marqueeActive && dx * dx + dy * dy >= threshold * threshold)
        {
            marqueeActive = true;
            model.BeginMarqueeSelection(pressModifiers);
        }
        if (marqueeActive)
            UpdateSelection();
        ShowMarquee();
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (cellGesture)
        {
            cellGesture = false;
            bool inside = CellAt(pointerArea.EntryIndex) is { } cell && new Rect(cell.Bounds.Size).Contains(e.GetPosition(cell));
            pointerArea.OnReleased(e, inside);
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (!tracking || Bridge is not { } model)
            return;
        if (marqueeActive)
        {
            pointer = Clamp(e.GetPosition(GridArea));
            UpdateSelection();
            model.EndMarqueeSelection();
        }
        else if ((pressModifiers & (KeyModifiers.Control | KeyModifiers.Shift)) == 0)
        {
            model.ClearEntrySelection();
        }
        tracking = false;
        marqueeActive = false;
        ShowMarquee();
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (cellGesture)
        {
            cellGesture = false;
            pointerArea.OnCanceled();
        }
        if (!tracking)
            return;
        if (marqueeActive)
            Bridge?.EndMarqueeSelection();
        tracking = false;
        marqueeActive = false;
        ShowMarquee();
    }

    private void OnGridKeyDown(object? sender, KeyEventArgs e)
    {
        if (Bridge is not { } model)
            return;
        int current = model.SelectedIndex;
        if (e.Key == Key.Enter)
        {
            if (current >= 0)
                model.ActivateEntry(current);
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Delete)
        {
            model.RequestDeleteSelection();
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            if (CellAt(current) is { } cell)
                RequestContextMenu(cell, new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2));
            else
                RequestContextMenu(MarqueeLayer, new Point(24, 24));
            e.Handled = true;
            return;
        }
        if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            model.SelectAllEntries();
            e.Handled = true;
            return;
        }
        string direction = e.Key switch
        {
            Key.Left => "left",
            Key.Right => "right",
            Key.Up => "up",
            Key.Down => "down",
            _ => "",
        };
        if (direction.Length == 0)
            return;
        int target = GridMath.NavigationTarget(current, Count, ColumnsVisible, direction);
        if (target >= 0)
        {
            model.SelectEntry(target, e.KeyModifiers & KeyModifiers.Shift);
            PositionViewAtIndex(target);
        }
        e.Handled = true;
    }
}

/// <summary>The grid's rows of cells: every container is an <see cref="EntryGridRow"/>.</summary>
public sealed class EntryGridRowList : ItemsControl
{
    internal EntryGrid? Owner { get; set; }

    protected override Type StyleKeyOverride => typeof(ItemsControl);

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = DefaultRecycleKey;
        return true;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new EntryGridRow();

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is EntryGridRow row)
            row.Attach(Owner, item is int value ? value : index);
    }

    protected override void ClearContainerForItemOverride(Control container)
    {
        base.ClearContainerForItemOverride(container);
        if (container is EntryGridRow row)
            row.Attach(null, -1);
    }
}

/// <summary>One row of <see cref="EntryGridCell"/>s at <c>leftMargin + column × cellWidth</c>.</summary>
public sealed class EntryGridRow : Panel
{
    private readonly List<EntryGridCell> cells = [];

    public EntryGridRow()
    {
        Height = Ui.Theme.GridCellHeight;
        HorizontalAlignment = HorizontalAlignment.Left;
    }

    public IReadOnlyList<EntryGridCell> Cells => cells;

    public EntryGridCell? CellAt(int column) => column >= 0 && column < cells.Count && cells[column].Item is not null ? cells[column] : null;

    internal void Attach(EntryGrid? grid, int row)
    {
        int columns = grid?.Columns ?? 0;
        while (cells.Count < columns)
        {
            var cell = new EntryGridCell();
            cells.Add(cell);
            Children.Add(cell);
        }
        IReadOnlyList<EntryItem>? items = grid?.Bridge?.EntriesModel;
        for (int column = 0; column < cells.Count; column++)
        {
            int index = row * columns + column;
            EntryItem? item = grid is not null && column < columns && row >= 0 && items is not null && index < items.Count
                ? items[index]
                : null;
            cells[column].Attach(grid, item, index);
        }
        Width = Ui.Theme.GridContentInset + columns * Ui.Theme.GridCellWidth;
        InvalidateArrange();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (EntryGridCell cell in cells)
            cell.Measure(new Size(Ui.Theme.GridCellWidth, Ui.Theme.GridCellHeight));
        return new Size(Width, Ui.Theme.GridCellHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        for (int column = 0; column < cells.Count; column++)
        {
            cells[column].Arrange(new Rect(Ui.Theme.GridContentInset + column * Ui.Theme.GridCellWidth, 0,
                Ui.Theme.GridCellWidth, Ui.Theme.GridCellHeight));
        }
        return finalSize;
    }
}

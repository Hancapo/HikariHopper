using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The details list (port of <c>EntryTable.qml</c>); its DataContext is the active tab's view model.
/// Rows are virtualized; pointer input is routed per row (<see cref="EntryPointerArea"/>) or, on the
/// background (below the rows and the panel's side insets), to the marquee.
/// </summary>
public partial class EntryTable : UserControl
{
    public const double TypeWidth = 200;
    public const double SizeWidth = 120;

    /// <summary>The empty lane at the end of every row that the floating scrollbar runs over.</summary>
    public const double TrailWidth = 14;

    /// <summary>A row's own padding before the name cell and after the trailing lane.</summary>
    public const double RowPaddingLeft = 10;
    public const double RowPaddingRight = 6;

    /// <summary>Rows sit <see cref="Ui.Theme.ListInset"/> inside the panel's left and right edges.</summary>
    public static readonly Thickness RowMargin = new(Ui.Theme.ListInset, 0);

    private readonly SmoothScrolling wheel;
    private readonly EntryPointerArea pointerArea;
    private readonly ListHighlightTracker highlight;
    private ExplorerViewModel? bridge;
    private INotifyCollectionChanged? entries;
    private int lastCount = -1;

    // marqueeArea
    private Point origin;
    private Point pointer;
    private bool tracking;
    private bool marqueeActive;
    private KeyModifiers pressModifiers;
    private bool rowGesture;

    public EntryTable()
    {
        InitializeComponent();
        wheel = SmoothScrolling.Attach(Scroller);
        pointerArea = new EntryPointerArea(() => Bridge, () => EntryList, RequestContextMenu);
        highlight = new ListHighlightTracker(Scroller, wheel, Ui.Theme.RowHeight);
        EntryList.Owner = this;
        ListArea.AddHandler(PointerPressedEvent, OnPointerPressed, handledEventsToo: false);
        ListArea.AddHandler(PointerMovedEvent, OnPointerMoved, handledEventsToo: false);
        ListArea.AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: false);
        ListArea.AddHandler(PointerCaptureLostEvent, OnCaptureLost, handledEventsToo: true);
        EntryList.KeyDown += OnListKeyDown;
        SizeChanged += (_, _) => UpdateColumns();
        Scroller.SizeChanged += (_, _) => UpdateColumns();
        Scroller.ScrollChanged += (_, e) =>
        {
            if (e.ExtentDelta.Y == 0 && e.ViewportDelta.Y == 0)
                return;
            foreach (Control container in EntryList.GetRealizedContainers())
                (container as EntryRow)?.RefreshFill();
        };
        UpdateColumns();
    }

    /// <summary>QML <c>contextMenuRequested(source, x, y)</c>, with the point in <c>source</c>'s coordinates.</summary>
    public event EventHandler<ContextMenuRequest>? ContextMenuRequested;

    internal ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    /// <summary>Whether the rows are search results, which show where each file lives.</summary>
    internal bool ShowsLocation => Bridge?.SearchShowsLocation == true;

    /// <summary>What the row leaves after its padding, TYPE, SIZE and the trailing lane.</summary>
    private double FreeWidth => RowWidth - RowPaddingLeft - TypeWidth - SizeWidth - TrailWidth - RowPaddingRight;

    /// <summary>
    /// The NAME column: all of the free width, or, for search results, about a third of it, the rest going to
    /// LOCATION.
    /// </summary>
    public double NameWidth => ShowsLocation ? Math.Clamp(FreeWidth * 0.32, 200, 360) : Math.Max(160, FreeWidth);

    /// <summary>The LOCATION column of search results; nothing otherwise.</summary>
    public double LocationWidth => ShowsLocation ? Math.Max(120, FreeWidth - NameWidth) : 0;

    /// <summary>The width of the rows and their columns, which span the panel.</summary>
    public double DataBlockWidth => RowWidth;

    /// <summary>The ListView's width: the whole panel, with the scrollbar floating over the trailing lane.</summary>
    internal double ListWidth => Math.Max(0, ListArea.Bounds.Width);

    internal double RowWidth => Math.Max(0, ListWidth - 2 * Ui.Theme.ListInset);

    /// <summary>How much of a row's right end the scrollbar covers; the fill stops short of it.</summary>
    internal double ScrollbarOverlap => QuietScrollBar.Overlap(Scroller, Ui.Theme.ListInset);

    internal Control FocusTarget => EntryList;

    /// <summary>The marquee while it is shown (for tests).</summary>
    internal SelectionMarquee MarqueeVisual => Marquee;

    internal bool MarqueeActive => marqueeActive;

    internal ScrollViewer ScrollViewer => Scroller;

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
        lastCount = bridge?.EntriesModel.Count ?? 0;
        // A new model regenerates the ListView at its start.
        highlight.Reset();
        wheel.ScrollTo(0);
        highlight.SetCurrentIndex(bridge?.SelectedIndex ?? -1);
        RefreshHeaders();
        RefreshEmptyState();
        RefreshRows();
    }

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ExplorerViewModel.SortColumn):
            case nameof(ExplorerViewModel.SortAscending):
                RefreshHeaders();
                break;
            case nameof(ExplorerViewModel.SearchQuery):
                RefreshRows();
                break;
            case nameof(ExplorerViewModel.HasWorkspace):
                RefreshEmptyState();
                break;
            case nameof(ExplorerViewModel.IsSearching):
            case nameof(ExplorerViewModel.SearchShowsLocation):
                UpdateColumns();
                RefreshHeaders();
                RefreshEmptyState();
                RefreshRows();
                break;
            case nameof(ExplorerViewModel.SearchError):
            case nameof(ExplorerViewModel.SearchHitCount):
            case nameof(ExplorerViewModel.SearchIndexing):
                RefreshEmptyState();
                break;
            case nameof(ExplorerViewModel.SelectedIndex):
                // currentIndex: bridge.selectedIndex — the ListView's highlight follows it; a selection given back by
                // a refresh in place doesn't move the view.
                if (Bridge?.KeepsListPosition == true)
                    highlight.Place(Bridge.SelectedIndex);
                else
                    highlight.SetCurrentIndex(Bridge?.SelectedIndex ?? -1);
                break;
        }
    }

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        int count = Bridge?.EntriesModel.Count ?? 0;
        // A model reset regenerates the view at its start; onCountChanged: positionViewAtBeginning(). Not for a
        // refresh in place (search results updated as the index fills in): the view stays where it was scrolled.
        bool inPlace = Bridge?.KeepsListPosition == true;
        if (e.Action == NotifyCollectionChangedAction.Reset && !inPlace)
            highlight.Reset();
        if ((e.Action == NotifyCollectionChangedAction.Reset || count != lastCount) && !inPlace)
            wheel.ScrollTo(0);
        lastCount = count;
        RefreshEmptyState();
    }

    private void UpdateColumns()
    {
        // The header has no inset, so its NAME cell also covers the row inset and padding.
        NameHeader.Width = Ui.Theme.ListInset + RowPaddingLeft + NameWidth;
        LocationHeader.IsVisible = ShowsLocation;
        LocationHeader.Width = LocationWidth;
        foreach (Control container in EntryList.GetRealizedContainers())
        {
            if (container is EntryRow row)
                row.UpdateLayoutWidths();
        }
    }

    private void RefreshHeaders()
    {
        ExplorerViewModel? model = Bridge;
        string column = model?.SortColumn ?? "";
        bool ascending = model?.SortAscending ?? true;
        NameHeader.SortActive = column == "name";
        LocationHeader.SortActive = column == "location";
        LocationHeader.SortAscending = ascending;
        TypeHeader.SortActive = column == "type";
        SizeHeader.SortActive = column == "size";
        NameHeader.SortAscending = TypeHeader.SortAscending = SizeHeader.SortAscending = ascending;
    }

    private void RefreshRows()
    {
        foreach (Control container in EntryList.GetRealizedContainers())
            (container as EntryRow)?.Refresh();
    }

    private void RefreshEmptyState()
    {
        bool hasWorkspace = Bridge?.HasWorkspace == true;
        EmptyState.IsVisible = (Bridge?.EntriesModel.Count ?? 0) == 0;
        if (Bridge is { IsSearching: true, SearchShowsLocation: false } folder)
        {
            EmptyText.Text = folder.SearchError.Length > 0 ? folder.SearchError
                : folder.SearchText.Length == 0 ? "This folder is empty" : "Nothing here matches";
            OpenGameButton.IsVisible = false;
            return;
        }
        if (Bridge is { IsSearching: true } model)
        {
            EmptyText.Text = model.SearchError.Length > 0 ? model.SearchError
                : model.SearchText.Length == 0 ? "Type a name to search"
                : model.SearchIndexing ? "Searching…" : "No files match";
            OpenGameButton.IsVisible = false;
            return;
        }
        EmptyText.Text = hasWorkspace ? "This folder is empty" : "Configure a GTA V installation to begin";
        OpenGameButton.IsVisible = !hasWorkspace;
    }

    private void OnSortName(object? sender, EventArgs e) => Bridge?.SortEntries("name");

    private void OnSortLocation(object? sender, EventArgs e) => Bridge?.SortEntries("location");

    private void OnSortType(object? sender, EventArgs e) => Bridge?.SortEntries("type");

    private void OnSortSize(object? sender, EventArgs e) => Bridge?.SortEntries("size");

    private void OnOpenConfiguredGame(object? sender, RoutedEventArgs e) => Bridge?.OpenConfiguredGame("");

    private void RequestContextMenu(Control source, Point point) =>
        ContextMenuRequested?.Invoke(this, new ContextMenuRequest(source, point));

    /// <summary><c>entryTable.indexAt(x - inset, y + contentY)</c> for a point in the list area.</summary>
    internal int RowAt(double x, double y)
    {
        double contentX = x - Ui.Theme.ListInset;
        double contentY = y + Scroller.Offset.Y;
        int count = Bridge?.EntriesModel.Count ?? 0;
        if (contentX < 0 || contentX >= RowWidth || contentY < 0)
            return -1;
        int row = (int)Math.Floor(contentY / Ui.Theme.RowHeight);
        return row < count ? row : -1;
    }

    private bool IsBackground(double x, double y) => RowAt(x, y) < 0;

    private double ClampX(double value) => Math.Max(0, Math.Min(ListWidth, value));

    private double ClampY(double value) => Math.Max(0, Math.Min(ListArea.Bounds.Height, value));

    private void UpdateSelection()
    {
        if (Bridge is not { } model)
            return;
        double left = Math.Min(origin.X, pointer.X);
        double right = Math.Max(origin.X, pointer.X);
        double top = Math.Min(origin.Y, pointer.Y);
        double bottom = Math.Max(origin.Y, pointer.Y);
        double height = ListArea.Bounds.Height;
        double contentHeight = model.EntriesModel.Count * Ui.Theme.RowHeight;
        double contentBottom = Math.Max(0, Math.Min(height, contentHeight - Scroller.Offset.Y));
        double rowsLeft = Ui.Theme.ListInset;
        if (left >= rowsLeft + RowWidth || right <= rowsLeft || top >= contentBottom)
        {
            model.UpdateMarqueeSelection(-1, -1);
            return;
        }
        double sampleBottom = Math.Max(top, Math.Min(bottom, contentBottom) - 1);
        int first = RowAt(rowsLeft + 1, top);
        int last = RowAt(rowsLeft + 1, sampleBottom);
        model.UpdateMarqueeSelection(first, last);
    }

    private void ShowMarquee()
    {
        Marquee.IsVisible = marqueeActive;
        if (marqueeActive)
            Marquee.Place(origin, pointer);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        Point point = e.GetPosition(ListArea);
        if (point.X >= ListWidth || Bridge is not { } model)
            return;
        PointerPointProperties properties = e.GetCurrentPoint(ListArea).Properties;
        bool background = IsBackground(point.X, point.Y);
        if (background && !model.HasWorkspace)
            return;
        if (!background)
        {
            int index = RowAt(point.X, point.Y);
            if (EntryList.ContainerFromIndex(index) is not EntryRow row)
                return;
            rowGesture = properties.IsLeftButtonPressed;
            pointerArea.OnPressed(e, index, model.EntriesModel[index].Selected, row, e.GetPosition(row));
            if (rowGesture)
                e.Pointer.Capture(ListArea);
            e.Handled = true;
            return;
        }
        if (properties.IsRightButtonPressed)
        {
            tracking = false;
            marqueeActive = false;
            ShowMarquee();
            model.ClearEntrySelection();
            EntryList.Focus(NavigationMethod.Pointer);
            RequestContextMenu(MarqueeLayer, point);
            e.Handled = true;
            return;
        }
        if (!properties.IsLeftButtonPressed)
            return;
        origin = new Point(ClampX(point.X), ClampY(point.Y));
        pointer = origin;
        pressModifiers = e.KeyModifiers;
        tracking = true;
        marqueeActive = false;
        ShowMarquee();
        EntryList.Focus(NavigationMethod.Pointer);
        e.Pointer.Capture(ListArea);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        Point point = e.GetPosition(ListArea);
        if (rowGesture)
        {
            if (pointerArea.Pressed && EntryList.ContainerFromIndex(pointerArea.EntryIndex) is EntryRow row)
                pointerArea.OnMoved(e, e.GetPosition(row));
            return;
        }
        if (!tracking || Bridge is not { } model)
            return;
        pointer = new Point(ClampX(point.X), ClampY(point.Y));
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
        Point point = e.GetPosition(ListArea);
        if (rowGesture)
        {
            rowGesture = false;
            int index = pointerArea.EntryIndex;
            bool inside = EntryList.ContainerFromIndex(index) is EntryRow row
                && new Rect(row.Bounds.Size).Contains(e.GetPosition(row));
            pointerArea.OnReleased(e, inside);
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }
        if (!tracking || Bridge is not { } model)
            return;
        if (marqueeActive)
        {
            pointer = new Point(ClampX(point.X), ClampY(point.Y));
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
        if (rowGesture)
        {
            rowGesture = false;
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

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (Bridge is not { } model)
            return;
        int current = model.SelectedIndex;
        int count = model.EntriesModel.Count;
        if (e.Key == Key.Enter)
        {
            if (current >= 0)
                model.ActivateEntry(current);
            e.Handled = true;
        }
        else if (e.Key == Key.Delete)
        {
            model.RequestDeleteSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Apps || (e.Key == Key.F10 && e.KeyModifiers.HasFlag(KeyModifiers.Shift)))
        {
            if (current >= 0 && EntryList.ContainerFromIndex(current) is EntryRow row)
                RequestContextMenu(row, new Point(row.Bounds.Width / 2, row.Bounds.Height / 2));
            else
                RequestContextMenu(EntryList, new Point(24, 24 - Scroller.Offset.Y));
            e.Handled = true;
        }
        else if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            model.SelectAllEntries();
            e.Handled = true;
        }
        else if (e.Key is Key.Up or Key.Down)
        {
            int direction = e.Key == Key.Up ? -1 : 1;
            int start = current >= 0 ? current : direction > 0 ? -1 : count;
            int target = Math.Max(0, Math.Min(count - 1, start + direction));
            if (target >= 0)
                model.SelectEntry(target, e.KeyModifiers & KeyModifiers.Shift);
            e.Handled = true;
        }
    }
}

/// <summary>A context-menu request: the point is in <see cref="Source"/>'s coordinates.</summary>
public sealed record ContextMenuRequest(Control Source, Point Point);

/// <summary>The table's ListView: every container is an <see cref="EntryRow"/>.</summary>
public sealed class EntryRowList : ItemsControl
{
    internal EntryTable? Owner { get; set; }

    protected override Type StyleKeyOverride => typeof(ItemsControl);

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = DefaultRecycleKey;
        return true;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new EntryRow();

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is EntryRow row)
            row.Attach(Owner, item as EntryItem);
    }

    protected override void ClearContainerForItemOverride(Control container)
    {
        base.ClearContainerForItemOverride(container);
        if (container is EntryRow row)
            row.Attach(Owner, null);
    }
}

/// <summary>
/// One table row (the QML delegate): a rounded selection/hover fill as wide as the row, short of
/// the scrollbar while it shows, the 16px glyph, the name with its matched characters marked, the
/// type in the UI face and the right-aligned size — for a folder, its item count in faint ink and
/// no type.
/// </summary>
public sealed class EntryRow : Panel
{
    private readonly RowFill fill;
    private readonly FileGlyph glyph = new();
    private readonly TextBlock name = Text(Ui.Theme.MonoFont, Ui.Theme.FontSize);
    private readonly TextBlock location = Text(Ui.Theme.MonoFont, 11);
    private readonly TextBlock type = Text(Ui.Theme.UiFont, Ui.Theme.FontSize);
    private readonly TextBlock size = Text(Ui.Theme.MonoFont, Ui.Theme.FontSize);
    private EntryTable? owner;
    private EntryItem? item;

    public EntryRow()
    {
        Height = Ui.Theme.RowHeight;
        HorizontalAlignment = HorizontalAlignment.Left;
        // The whole row is the delegate's MouseArea, empty cells included.
        Background = Brushes.Transparent;
        fill = new RowFill(this);
        name.TextTrimming = TextTrimming.CharacterEllipsis;
        type.TextTrimming = TextTrimming.CharacterEllipsis;
        location.TextTrimming = TextTrimming.CharacterEllipsis;
        location.IsVisible = false;
        type.Padding = new Thickness(12, 0, 0, 0);
        size.Padding = new Thickness(0, 0, 12, 0);
        size.TextAlignment = TextAlignment.Right;
        Children.Add(fill);
        Children.Add(glyph);
        Children.Add(name);
        Children.Add(location);
        Children.Add(type);
        Children.Add(size);
    }

    public EntryItem? Item => item;

    private static TextBlock Text(FontFamily family, double fontSize) => new()
    {
        FontFamily = family,
        FontSize = fontSize,
        VerticalAlignment = VerticalAlignment.Center,
    };

    internal void Attach(EntryTable? table, EntryItem? entry)
    {
        if (item is not null)
            item.PropertyChanged -= OnItemChanged;
        owner = table;
        item = entry;
        if (item is not null)
            item.PropertyChanged += OnItemChanged;
        UpdateLayoutWidths();
        Refresh();
    }

    internal void UpdateLayoutWidths()
    {
        if (owner is not null)
            Width = owner.RowWidth;
        InvalidateMeasure();
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    internal void Refresh()
    {
        if (item is null)
            return;
        bool selected = item.Selected;
        glyph.Kind = item.IsDirectory ? "folder" : item.Kind.Contains("Package", StringComparison.Ordinal) ? "archive" : "file";
        glyph.FileKind = item.Kind;
        glyph.Selected = selected;
        name.Foreground = selected ? Ui.Theme.SelectionText : Ui.Theme.TextRow;
        name.FontWeight = selected ? FontWeight.Bold : FontWeight.Normal;
        IBrush mark = selected ? Ui.Theme.SelectionText : Ui.Theme.Brass;
        if (item.Record.NativeEntry is Core.Search.SearchHit hit)
        {
            EntryText.MarkSpans(name.Inlines!, item.Name, hit.Spans, mark);
            EntryText.Location(location.Inlines!, hit.Location, selected);
            location.IsVisible = true;
        }
        else if (owner?.Bridge is { IsSearching: true } searching)
        {
            // The folder's own search marks what its pattern matched.
            EntryText.MarkSpans(name.Inlines!, item.Name, searching.SearchSpans(item.Name), mark);
            if (location.IsVisible)
            {
                location.Inlines!.Clear();
                location.IsVisible = false;
            }
        }
        else
        {
            EntryText.MarkMatch(name.Inlines!, item.Name, owner?.Bridge?.SearchQuery, mark);
            if (location.IsVisible)
            {
                location.Inlines!.Clear();
                location.IsVisible = false;
            }
        }
        type.Text = item.TypeText;
        type.Foreground = selected ? Ui.Theme.SelectionInk : Ui.Theme.TextDim;
        size.Text = item.SizeText;
        size.Foreground = selected ? Ui.Theme.SelectionInk : item.IsDirectory ? Ui.Theme.TextFaint : Ui.Theme.TextRow;
        AutomationProperties.SetName(this, item.Name);
        InvalidateMeasure();
        fill.InvalidateVisual();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty)
            fill.InvalidateVisual();
    }

    internal void RefreshFill() => fill.InvalidateVisual();

    private double NameWidth => owner?.NameWidth ?? 300;

    private double LocationWidth => owner?.LocationWidth ?? 0;

    protected override Size MeasureOverride(Size availableSize)
    {
        var unbounded = new Size(double.PositiveInfinity, Ui.Theme.RowHeight);
        glyph.Measure(unbounded);
        name.Measure(new Size(NameCellAvailable(), Ui.Theme.RowHeight));
        location.Measure(new Size(Math.Max(0, LocationWidth - 8), Ui.Theme.RowHeight));
        type.Measure(new Size(EntryTable.TypeWidth, Ui.Theme.RowHeight));
        size.Measure(new Size(EntryTable.SizeWidth, Ui.Theme.RowHeight));
        fill.Measure(unbounded);
        return new Size(owner?.RowWidth ?? 0, Ui.Theme.RowHeight);
    }

    /// <summary>The name cell after the glyph, less an 8px margin before TYPE.</summary>
    private double NameCellAvailable() => Math.Max(0, NameWidth - NameOffset + EntryTable.RowPaddingLeft - 8);

    /// <summary>The name text starts after the 16px glyph and a 10px gap.</summary>
    private const double NameOffset = EntryTable.RowPaddingLeft + 16 + 10;

    protected override Size ArrangeOverride(Size finalSize)
    {
        double height = finalSize.Height;
        double typeLeft = EntryTable.RowPaddingLeft + NameWidth + LocationWidth;
        fill.Arrange(new Rect(finalSize));
        double locationHeight = location.DesiredSize.Height;
        location.Arrange(new Rect(EntryTable.RowPaddingLeft + NameWidth, QtTextCenter(height, locationHeight),
            Math.Min(location.DesiredSize.Width, Math.Max(0, LocationWidth - 8)), locationHeight));
        glyph.Arrange(new Rect(EntryTable.RowPaddingLeft, QtCenter(height, 16), 16, 16));
        // Text { width: Math.min(implicitWidth, available); anchors.verticalCenter }.
        double nameWidth = Math.Min(name.DesiredSize.Width, NameCellAvailable());
        double nameHeight = name.DesiredSize.Height;
        name.Arrange(new Rect(NameOffset, QtCenter(height, nameHeight), nameWidth, nameHeight));
        // Full-height Texts with AlignVCenter: Qt places the line box half a pixel lower when it can't centre.
        double typeHeight = type.DesiredSize.Height;
        type.Arrange(new Rect(typeLeft, QtTextCenter(height, typeHeight), EntryTable.TypeWidth, typeHeight));
        double sizeHeight = size.DesiredSize.Height;
        size.Arrange(new Rect(typeLeft + EntryTable.TypeWidth, QtTextCenter(height, sizeHeight), EntryTable.SizeWidth, sizeHeight));
        return finalSize;
    }

    /// <summary>Text.AlignVCenter inside a taller Text item: the half-pixel remainder goes down.</summary>
    internal static double QtTextCenter(double parent, double child) => Math.Floor((parent - child) / 2 + 0.5);

    /// <summary>Qt anchors.verticalCenter with alignWhenCentered: an odd height rounds its half up.</summary>
    internal static double QtCenter(double parent, double child) => parent / 2 - Math.Ceiling(Math.Round(child) / 2);

    private sealed class RowFill(EntryRow row) : Control
    {
        public override void Render(DrawingContext context)
        {
            IBrush? brush = row.item?.Selected == true ? Ui.Theme.Selection
                : row.IsPointerOver ? Ui.Theme.HoverBg
                : null;
            double width = Math.Max(0, Bounds.Width - (row.owner?.ScrollbarOverlap ?? 0));
            if (brush is not null)
                context.DrawRectangle(brush, null, new Rect(0, 0, width, Bounds.Height), Ui.Theme.ItemRadius, Ui.Theme.ItemRadius);
        }
    }
}

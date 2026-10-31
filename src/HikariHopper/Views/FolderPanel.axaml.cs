using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The folder tree pane (port of <c>FolderPanel.qml</c>); its DataContext is the active tab's view
/// model. Rows are virtualized; <c>currentIndex</c> follows <see cref="ExplorerViewModel.TreeFocusIndex"/>.
/// </summary>
public partial class FolderPanel : UserControl
{
    /// <summary>SplitView.minimumWidth.</summary>
    public const double MinimumWidth = 210;

    /// <summary>SplitView.preferredWidth.</summary>
    public const double PreferredWidth = 247;

    private readonly QtWheelScrolling wheel;
    private ExplorerViewModel? bridge;

    public FolderPanel()
    {
        InitializeComponent();
        wheel = QtWheelScrolling.Attach(Scroller);
        FolderList.Owner = this;
        FolderList.KeyDown += OnListKeyDown;
    }

    internal ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    /// <summary>ListView.currentIndex.</summary>
    internal int CurrentIndex => Bridge?.TreeFocusIndex ?? -1;

    internal Control FocusTarget => FolderList;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (bridge is not null)
            bridge.PropertyChanged -= OnBridgePropertyChanged;
        bridge = Bridge;
        if (bridge is not null)
            bridge.PropertyChanged += OnBridgePropertyChanged;
        RefreshRows();
    }

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExplorerViewModel.TreeFocusIndex))
            return;
        RefreshRows();
        // The current item stays in view (ListView SetIndex tracking), and onTreeFocusChanged
        // repeats it once the rows have settled (Qt.callLater positionViewAtIndex Contain).
        PositionCurrent();
        Dispatcher.UIThread.Post(PositionCurrent, DispatcherPriority.Loaded);
    }

    private void PositionCurrent()
    {
        int index = CurrentIndex;
        if (index >= 0)
            wheel.EnsureVisible(index * Ui.Theme.RowHeight, (index + 1) * Ui.Theme.RowHeight);
    }

    private void RefreshRows()
    {
        foreach (Control container in FolderList.GetRealizedContainers())
            (container as FolderRow)?.Refresh();
    }

    private void OnListKeyDown(object? sender, KeyEventArgs e)
    {
        if (Bridge is not { } model)
            return;
        int current = CurrentIndex;
        int count = model.TreeModel.Count;
        if (e.Key == Key.Enter)
        {
            if (current >= 0)
                model.ActivateTreeRow(current);
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Up or Key.Down))
            return;
        int direction = e.Key == Key.Up ? -1 : 1;
        int start = current >= 0 ? current : direction > 0 ? -1 : count;
        int target = Math.Max(0, Math.Min(count - 1, start + direction));
        if (target >= 0)
            model.FocusTreeRow(target);
        e.Handled = true;
    }
}

/// <summary>The tree's ListView: every container is a <see cref="FolderRow"/> that knows its index.</summary>
public sealed class FolderTreeList : ItemsControl
{
    internal FolderPanel? Owner { get; set; }

    protected override Type StyleKeyOverride => typeof(ItemsControl);

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = DefaultRecycleKey;
        return true;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey) => new FolderRow();

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is FolderRow row && Owner is { } owner)
        {
            row.Item = item as TreeItem;
            row.Attach(owner, index);
        }
    }

    protected override void ContainerIndexChangedOverride(Control container, int oldIndex, int newIndex)
    {
        base.ContainerIndexChangedOverride(container, oldIndex, newIndex);
        if (container is FolderRow row && Owner is { } owner)
            row.Attach(owner, newIndex);
    }
}

/// <summary>
/// One tree row (the QML delegate), drawn the way Qt draws it: the row fill and ring, one dotted
/// guide per depth level at <c>14 + level × 15</c>, the 9px [+]/[−] box in a 16px toggle cell at
/// <c>7 + depth × 15</c>, the 16px glyph and the mono label.
/// </summary>
public sealed class FolderRow : Panel
{
    public static readonly StyledProperty<TreeItem?> ItemProperty =
        AvaloniaProperty.Register<FolderRow, TreeItem?>(nameof(Item));

    private readonly FileGlyph glyph = new();
    private readonly TextBlock label = new()
    {
        FontFamily = Ui.Theme.MonoFont,
        FontSize = Ui.Theme.FontSize,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly ToggleTarget toggle;
    private FolderPanel? panel;
    private TreeItem? subscribed;
    private bool toggleHovered;
    private bool togglePressed;
    private bool rowPressed;
    private bool doubleClicked;

    public FolderRow()
    {
        Height = Ui.Theme.RowHeight;
        Background = Brushes.Transparent;
        toggle = new ToggleTarget(this);
        Children.Add(new RowArt(this));
        Children.Add(glyph);
        Children.Add(label);
        Children.Add(toggle);
    }

    public TreeItem? Item
    {
        get => GetValue(ItemProperty);
        set => SetValue(ItemProperty, value);
    }

    /// <summary>The model row this delegate shows.</summary>
    public int Index { get; private set; } = -1;

    public bool IsSelected => Item?.Selected == true;

    /// <summary><c>folderList.currentIndex === index &amp;&amp; !selected</c>.</summary>
    public bool IsFocusedRow => panel is not null && panel.CurrentIndex == Index && Index >= 0 && !IsSelected;

    private int Depth => Item?.Depth ?? 0;

    private bool HasChildren => Item?.HasChildren == true;

    private double RowLeft => 7 + Depth * 15;

    private Rect ToggleRect => new(RowLeft, 0, 16, Bounds.Height);

    internal void Attach(FolderPanel owner, int index)
    {
        panel = owner;
        Index = index;
        Refresh();
    }

    internal void Refresh()
    {
        TreeItem? item = Item;
        bool archive = item?.Kind == "archive";
        glyph.Kind = archive ? "archive" : "folder";
        label.Text = item?.Label ?? "";
        label.Foreground = IsSelected ? Ui.Theme.Text : Ui.Theme.TextRow;
        label.FontWeight = item?.Kind == "root" || archive ? FontWeight.Bold : FontWeight.Normal;
        toggle.Focusable = HasChildren;
        toggle.IsTabStop = HasChildren;
        AutomationProperties.SetName(toggle, item is null ? "" : item.Expanded ? $"Collapse {item.Label}" : $"Expand {item.Label}");
        InvalidateArrange();
        Children[0].InvalidateVisual();
    }

    internal void Toggle()
    {
        if (HasChildren && Item is { } item)
            panel?.Bridge?.ToggleTreeNode(item.Path);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ItemProperty)
        {
            if (subscribed is not null)
                subscribed.PropertyChanged -= OnItemChanged;
            subscribed = Item;
            if (subscribed is not null)
                subscribed.PropertyChanged += OnItemChanged;
            Refresh();
        }
        else if (change.Property == IsPointerOverProperty)
        {
            if (!IsPointerOver)
                SetToggleHovered(false);
            Children[0].InvalidateVisual();
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e) => Refresh();

    protected override Size MeasureOverride(Size availableSize)
    {
        glyph.Measure(availableSize);
        label.Measure(new Size(Math.Max(0, (double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width) - 50), Ui.Theme.RowHeight));
        toggle.Measure(availableSize);
        return new Size(0, Ui.Theme.RowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double left = RowLeft;
        glyph.Arrange(new Rect(left + 21, Math.Floor((finalSize.Height - 16) / 2), 16, 16));
        // Text { x: 42 (in the Row); width: Math.max(0, folderDelegate.width - x - 8) }: the width
        // ignores the depth indent, so deep labels run to the list's clip instead of eliding early.
        label.Arrange(new Rect(left + 42, 0, Math.Max(0, finalSize.Width - 50), finalSize.Height));
        toggle.Arrange(ToggleRect);
        Children[0].Arrange(new Rect(finalSize));
        return finalSize;
    }

    private void Draw(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        bool selected = IsSelected;
        bool focused = IsFocusedRow;
        IBrush? fill = selected ? Ui.Theme.SelectionWash
            : focused ? Ui.Theme.BorderSoft
            : IsPointerOver ? Ui.Theme.HoverChrome
            : null;
        if (selected || focused)
        {
            // Qt draws the border outside the inner fill, so translucent rings do not stack.
            IBrush ring = selected ? Ui.Theme.SelectionRing : Ui.Theme.Border;
            context.FillRectangle(fill!, bounds.Deflate(1));
            context.FillRectangle(ring, new Rect(0, 0, bounds.Width, 1));
            context.FillRectangle(ring, new Rect(0, bounds.Height - 1, bounds.Width, 1));
            context.FillRectangle(ring, new Rect(0, 1, 1, bounds.Height - 2));
            context.FillRectangle(ring, new Rect(bounds.Width - 1, 1, 1, bounds.Height - 2));
        }
        else if (fill is not null)
        {
            context.FillRectangle(fill, bounds);
        }

        // Dotted guides, one per level of depth.
        for (int level = 0; level < Depth; level++)
        {
            double x = 14 + level * 15;
            for (double y = 0; y < bounds.Height; y += 3)
                context.FillRectangle(Ui.Theme.Guide, new Rect(x, y, 1, 1));
        }

        if (!HasChildren)
            return;
        // Drawn [+] / [−] box, not a typed character. anchors.centerIn with alignWhenCentered.
        IBrush ink = toggleHovered ? Ui.Theme.Text : Ui.Theme.TextDim;
        double boxX = RowLeft + 3;
        double boxY = Math.Floor(bounds.Height / 2) - 5;
        context.FillRectangle(ink, new Rect(boxX, boxY, 9, 9));
        context.FillRectangle(Ui.Theme.SidebarBg, new Rect(boxX + 1, boxY + 1, 7, 7));
        context.FillRectangle(ink, new Rect(boxX + 2, boxY + 4, 5, 1));
        if (Item?.Expanded != true)
            context.FillRectangle(ink, new Rect(boxX + 4, boxY + 2, 1, 5));
    }

    private void SetToggleHovered(bool hovered)
    {
        if (hovered == toggleHovered)
            return;
        toggleHovered = hovered;
        Cursor = hovered ? new Cursor(StandardCursorType.Hand) : null;
        Children[0].InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        SetToggleHovered(HasChildren && ToggleRect.Contains(e.GetPosition(this)));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        e.Handled = true;
        e.Pointer.Capture(this);
        if (HasChildren && ToggleRect.Contains(e.GetPosition(this)))
        {
            // The toggle's MouseArea has no double-click handler, so every click toggles.
            togglePressed = true;
            return;
        }
        rowPressed = true;
        doubleClicked = e.ClickCount == 2;
        if (doubleClicked)
            panel?.Bridge?.ActivateTreeRow(Index);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton != MouseButton.Left || (!togglePressed && !rowPressed))
            return;
        e.Handled = true;
        // Releasing the capture raises OnPointerCaptureLost synchronously, which clears the press
        // state, so read it first.
        bool wasToggle = togglePressed;
        togglePressed = false;
        rowPressed = false;
        e.Pointer.Capture(null);
        Point point = e.GetPosition(this);
        if (wasToggle)
        {
            if (ToggleRect.Contains(point))
                Toggle();
            return;
        }
        if (doubleClicked || !new Rect(Bounds.Size).Contains(point) || Item is not { } item || panel is null)
            return;
        panel.Bridge?.FocusTreePath(item.Path);
        panel.FocusTarget.Focus(NavigationMethod.Pointer);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        togglePressed = false;
        rowPressed = false;
    }

    /// <summary>Paints the row's fill, ring, guides and toggle box beneath the glyph and label.</summary>
    private sealed class RowArt(FolderRow row) : Control
    {
        public override void Render(DrawingContext context) => row.Draw(context);
    }

    /// <summary>The [+]/[−] cell's keyboard target (activeFocusOnTab when the node has children).</summary>
    private sealed class ToggleTarget : Control
    {
        private readonly FolderRow row;

        public ToggleTarget(FolderRow row)
        {
            this.row = row;
            IsHitTestVisible = false;
            FocusAdorner = null;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key is Key.Space or Key.Enter)
            {
                row.Toggle();
                e.Handled = true;
            }
        }
    }
}

using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>The browser-style tab row (port of <c>TabStrip.qml</c>); its DataContext is the tabs.</summary>
public partial class TabStrip : UserControl
{
    private ExplorerTabsViewModel? tabs;

    public TabStrip()
    {
        InitializeComponent();
        TabList.SizeChanged += (_, _) => PositionActiveTab();
    }

    private ExplorerTabsViewModel? Tabs => DataContext as ExplorerTabsViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (tabs is not null)
        {
            tabs.PropertyChanged -= OnTabsPropertyChanged;
            tabs.Tabs.CollectionChanged -= OnTabsChanged;
        }
        tabs = Tabs;
        if (tabs is not null)
        {
            tabs.PropertyChanged += OnTabsPropertyChanged;
            tabs.Tabs.CollectionChanged += OnTabsChanged;
        }
    }

    /// <summary><c>Layout.maximumWidth: strip.width - 45</c> for the tab list.</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        if (!double.IsInfinity(availableSize.Width))
            TabList.MaxWidth = Math.Max(0, availableSize.Width - 45);
        return base.MeasureOverride(availableSize);
    }

    private void OnTabsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // onCurrentIndexChanged: positionViewAtIndex(currentIndex, ListView.Contain)
        if (e.PropertyName == nameof(ExplorerTabsViewModel.ActiveIndex))
        {
            RefreshTabs();
            Dispatcher.UIThread.Post(PositionActiveTab, DispatcherPriority.Loaded);
        }
    }

    // A tab's spacing and divider depend on its neighbours.
    private void OnTabsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        Dispatcher.UIThread.Post(RefreshTabs, DispatcherPriority.Loaded);

    private void PositionActiveTab()
    {
        if (tabs is null || TabItems.ContainerFromIndex(tabs.ActiveIndex) is not { } container)
            return;
        double left = container.Bounds.X;
        double right = container.Bounds.Right;
        double offset = TabList.Offset.X;
        double viewport = TabList.Viewport.Width;
        if (left < offset)
            offset = left;
        else if (right > offset + viewport)
            offset = right - viewport;
        TabList.Offset = TabList.Offset.WithX(offset);
    }

    private void OnNewTab(object? sender, RoutedEventArgs e) => Tabs?.NewTab();

    internal void CloseTab(ExplorerViewModel tab)
    {
        if (Tabs is { } owner && owner.Tabs.IndexOf(tab) is var index and >= 0)
            owner.CloseTab(index);
    }

    internal void ActivateTab(ExplorerViewModel tab)
    {
        if (Tabs is { } owner && owner.Tabs.IndexOf(tab) is var index and >= 0)
            owner.ActivateTab(index);
    }

    /// <summary>The tab's position in the strip, or -1.</summary>
    internal int IndexOf(ExplorerViewModel tab) => Tabs?.Tabs.IndexOf(tab) ?? -1;

    /// <summary>The tab drawn at <paramref name="index"/>, when it is realized.</summary>
    internal TabStripTab? TabAt(int index) =>
        index >= 0 && TabItems.ContainerFromIndex(index) is ContentPresenter { Child: TabStripTab tab } ? tab : null;

    /// <summary>Re-evaluates every tab's spacing, divider and hover state.</summary>
    internal void RefreshTabs()
    {
        foreach (Control container in TabItems.GetRealizedContainers())
        {
            if (container is ContentPresenter { Child: TabStripTab tab })
                tab.Refresh();
        }
    }
}

/// <summary>
/// One tab of <see cref="TabStrip"/> (the QML delegate), <see cref="Ui.Theme.TabWidth"/> wide: a
/// 15px glyph, the mono title (elided) and a 20px close mark pinned to the right; clicking
/// anywhere left of the close mark activates the tab.
/// </summary>
/// <remarks>
/// Every tab has the same shape, the way a browser draws them: <see cref="Ui.Theme.TabHeight"/>
/// tall on the strip's floor with 8px top corners and a foot that curves out 8px into the toolbar
/// on both sides. The active tab fills it with the toolbar's colour and sits on top; a hovered
/// tab fills it a shade lighter beneath the active one; the rest are bare, with a 1px divider
/// between two of them.
/// </remarks>
public sealed class TabStripTab : Decorator
{
    public static readonly StyledProperty<ExplorerViewModel?> TabProperty =
        AvaloniaProperty.Register<TabStripTab, ExplorerViewModel?>(nameof(Tab));

    /// <summary>A filled tab's foot curves out this far on each side.</summary>
    public const double FootRadius = Ui.Theme.TabRadius;

    private readonly FileGlyph glyph = new() { Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center };
    private readonly EditionMark edition = new() { Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock tip = new() { FontFamily = Ui.Theme.UiFont, FontSize = Ui.Theme.FontSize, Foreground = Ui.Theme.Text };
    private readonly TextBlock title = new()
    {
        FontFamily = Ui.Theme.MonoFont,
        FontSize = Ui.Theme.FontSize,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly ChromeToolButton close = new()
    {
        Width = 20,
        Height = 20,
        VerticalAlignment = VerticalAlignment.Center,
        Bordered = false,
        CornerRadius = Ui.Theme.InnerCorner,
        IconKind = "close",
        IconSize = 12,
        IconThickness = 1.4,
    };
    private readonly Border clickArea = new() { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 27, 0) };
    private ExplorerViewModel? subscribed;
    private bool pressed;
    private bool divider;

    public TabStripTab()
    {
        VerticalAlignment = VerticalAlignment.Bottom;
        var row = new Grid
        {
            Margin = new Thickness(12, 0, 7, 0),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 9,
        };
        row.Children.Add(glyph);
        row.Children.Add(edition);
        Grid.SetColumn(title, 1);
        row.Children.Add(title);
        Grid.SetColumn(close, 2);
        row.Children.Add(close);
        var panel = new Panel();
        panel.Children.Add(row);
        panel.Children.Add(clickArea);
        Child = panel;
        close.Click += (_, _) =>
        {
            if (Tab is { } tab)
                Strip?.CloseTab(tab);
        };
        clickArea.PointerPressed += OnPressed;
        clickArea.PointerReleased += OnReleased;
        clickArea.PointerCaptureLost += (_, _) => pressed = false;
    }

    /// <summary>The game edition's mark, shown in place of the glyph on a tab with a game open.</summary>
    internal EditionMark EditionMark => edition;

    /// <summary>The folder or archive glyph of a tab without a game.</summary>
    internal FileGlyph Glyph => glyph;

    /// <summary>Where the tooltip shows: the tab left of its close mark.</summary>
    internal Control TipHost => clickArea;

    /// <summary>The tooltip's text: the full name, then the edition and the path where there are any.</summary>
    internal string TipText => tip.Text ?? "";

    public ExplorerViewModel? Tab
    {
        get => GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    /// <summary>True for the active tab, which merges into the toolbar.</summary>
    public bool Active => Tab?.TabActive == true;

    /// <summary>The 1px rule drawn on the left edge, between two inactive tabs.</summary>
    public bool DividerVisible => divider;

    private TabStrip? Strip => this.FindAncestorOfType<TabStrip>();

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = new Size(Ui.Theme.TabWidth, Ui.Theme.TabHeight);
        base.MeasureOverride(size);
        return size;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TabProperty)
        {
            if (subscribed is not null)
                subscribed.PropertyChanged -= OnTabChanged;
            subscribed = Tab;
            if (subscribed is not null)
                subscribed.PropertyChanged += OnTabChanged;
            Refresh();
        }
        else if (change.Property == IsPointerOverProperty)
        {
            // The neighbour's divider gives way to the hovered tab, too.
            if (Strip is { } strip)
                strip.RefreshTabs();
            else
                Refresh();
        }
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExplorerViewModel.TabTitle) or nameof(ExplorerViewModel.TabActive)
            or nameof(ExplorerViewModel.GameEdition) or nameof(ExplorerViewModel.HasArchive))
            Refresh();
    }

    internal void Refresh()
    {
        string text = Tab?.TabTitle ?? "";
        bool active = Active;
        bool hovered = IsPointerOver;
        TabStrip? strip = Strip;
        int index = Tab is null || strip is null ? -1 : strip.IndexOf(Tab);
        TabStripTab? previous = strip?.TabAt(index - 1);
        divider = !active && !hovered && previous is { Active: false, IsPointerOver: false };
        // The active tab's feet overlap its neighbours, a hovered tab's feet overlap bare ones.
        if (Parent is Control host)
            host.ZIndex = active ? 2 : hovered ? 1 : 0;
        bool game = Tab?.HasGame == true;
        edition.IsVisible = game;
        edition.Enhanced = Tab?.GameEdition == "GTA V Enhanced";
        glyph.IsVisible = !game;
        glyph.Kind = text.EndsWith(".rpf", StringComparison.Ordinal) ? "archive" : "folder";
        RefreshTip(text);
        title.Text = text;
        title.Foreground = active || hovered ? Ui.Theme.Text : Ui.Theme.TextDim;
        title.FontWeight = active ? FontWeight.Bold : FontWeight.Normal;
        if (active || hovered)
            close.ClearValue(TemplatedControl.ForegroundProperty);
        else
            close.Foreground = Ui.Theme.TextFaint;
        AutomationProperties.SetName(close, $"Close {text}");
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void RefreshTip(string text)
    {
        ExplorerViewModel? tab = Tab;
        string[] lines = tab is null ? []
            : tab.HasGame ? [text, tab.GameEdition, tab.GamePath]
            : tab.HasArchive ? [text, tab.ArchivePath]
            : [];
        tip.Text = string.Join('\n', lines.Where(line => line.Length > 0).Distinct());
        // Built on the first tab that has something to say, the tooltip stays and follows the text.
        if (tip.Text.Length > 0 && ToolTip.GetTip(clickArea) is null)
        {
            ToolTip.SetTip(clickArea, new ToolTip
            {
                Content = tip,
                Background = Ui.Theme.ChromeRaised,
                BorderBrush = Ui.Theme.Border,
                BorderThickness = new Thickness(1),
                CornerRadius = Ui.Theme.ControlCorner,
                Padding = new Thickness(8, 5),
                MaxWidth = 640,
            });
        }
        ToolTip.SetServiceEnabled(clickArea, tip.Text.Length > 0);
    }

    public override void Render(DrawingContext context)
    {
        double w = Bounds.Width;
        double h = Bounds.Height;
        if (Active || IsPointerOver)
        {
            context.DrawGeometry(Active ? Ui.Theme.ChromeBg : Ui.Theme.TabBg, null, TabShape(w, h));
            return;
        }
        if (divider)
            context.FillRectangle(Ui.Theme.BorderSoft, new Rect(-1, Math.Floor(h / 2) - 8, 1, 16));
    }

    /// <summary>
    /// A filled tab's outline: rounded top corners and a foot that curves out into the toolbar on
    /// both sides, extending <see cref="FootRadius"/> beyond the tab's bounds.
    /// </summary>
    internal static Geometry TabShape(double w, double h)
    {
        double r = FootRadius;
        var geometry = new StreamGeometry();
        using (StreamGeometryContext path = geometry.Open())
        {
            path.BeginFigure(new Point(-r, h), true);
            path.ArcTo(new Point(0, h - r), new Size(r, r), 0, false, SweepDirection.CounterClockwise);
            path.LineTo(new Point(0, r));
            path.ArcTo(new Point(r, 0), new Size(r, r), 0, false, SweepDirection.Clockwise);
            path.LineTo(new Point(w - r, 0));
            path.ArcTo(new Point(w, r), new Size(r, r), 0, false, SweepDirection.Clockwise);
            path.LineTo(new Point(w, h - r));
            path.ArcTo(new Point(w + r, h), new Size(r, r), 0, false, SweepDirection.CounterClockwise);
            path.EndFigure(true);
        }
        return geometry;
    }

    private void OnPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(clickArea).Properties.IsLeftButtonPressed)
            return;
        pressed = true;
        e.Pointer.Capture(clickArea);
        e.Handled = true;
    }

    private void OnReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!pressed || e.InitialPressMouseButton != MouseButton.Left)
            return;
        pressed = false;
        e.Pointer.Capture(null);
        if (new Rect(clickArea.Bounds.Size).Contains(e.GetPosition(clickArea)) && Tab is { } tab)
            Strip?.ActivateTab(tab);
        e.Handled = true;
    }
}

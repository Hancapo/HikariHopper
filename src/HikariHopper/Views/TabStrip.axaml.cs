using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
            tabs.PropertyChanged -= OnTabsPropertyChanged;
        tabs = Tabs;
        if (tabs is not null)
            tabs.PropertyChanged += OnTabsPropertyChanged;
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
            Dispatcher.UIThread.Post(PositionActiveTab, DispatcherPriority.Loaded);
    }

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
}

/// <summary>
/// One tab of <see cref="TabStrip"/> (the QML delegate): <c>max(150, min(220, title width + 66))</c>
/// wide, tabActive/hoverChrome/tabBg fill with an accent/guide/border edge, a 16px glyph, the mono
/// title and a 20px close mark; clicking anywhere left of the close mark activates the tab.
/// </summary>
public sealed class TabStripTab : Border
{
    public static readonly StyledProperty<ExplorerViewModel?> TabProperty =
        AvaloniaProperty.Register<TabStripTab, ExplorerViewModel?>(nameof(Tab));

    private readonly FileGlyph glyph = new() { VerticalAlignment = VerticalAlignment.Center };
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
        Height = double.NaN,
        VerticalAlignment = VerticalAlignment.Stretch,
        Bordered = false,
        HoverFill = false,
        IconKind = "close",
    };
    private readonly Border clickArea = new() { Background = Brushes.Transparent, Margin = new Thickness(0, 0, 24, 0) };
    private ExplorerViewModel? subscribed;
    private bool pressed;

    public TabStripTab()
    {
        BorderThickness = new Thickness(1);
        var row = new Grid
        {
            // anchors.fill: parent; leftMargin 11; rightMargin 4 — the border does not inset it.
            Margin = new Thickness(10, -1, 3, -1),
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            ColumnSpacing = 7,
        };
        row.Children.Add(glyph);
        Grid.SetColumn(title, 1);
        row.Children.Add(title);
        Grid.SetColumn(close, 2);
        row.Children.Add(close);
        var panel = new Panel();
        panel.Children.Add(row);
        panel.Children.Add(clickArea);
        Child = panel;
        clickArea.Margin = new Thickness(-1, -1, 23, -1);
        close.Click += (_, _) =>
        {
            if (Tab is { } tab)
                Strip?.CloseTab(tab);
        };
        clickArea.PointerPressed += OnPressed;
        clickArea.PointerReleased += OnReleased;
        clickArea.PointerCaptureLost += (_, _) => pressed = false;
    }

    public ExplorerViewModel? Tab
    {
        get => GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    private TabStrip? Strip => this.FindAncestorOfType<TabStrip>();

    protected override Size MeasureOverride(Size availableSize)
    {
        title.Measure(Size.Infinity);
        double width = Math.Max(150, Math.Min(220, title.DesiredSize.Width + 66));
        base.MeasureOverride(new Size(width, availableSize.Height));
        return new Size(width, double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
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
            Refresh();
        }
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExplorerViewModel.TabTitle) or nameof(ExplorerViewModel.TabActive))
            Refresh();
    }

    private void Refresh()
    {
        string text = Tab?.TabTitle ?? "";
        bool active = Tab?.TabActive == true;
        bool hovered = IsPointerOver;
        Background = active ? Ui.Theme.TabActive : hovered ? Ui.Theme.HoverChrome : Ui.Theme.TabBg;
        BorderBrush = active ? Ui.Theme.BorderAccent : hovered ? Ui.Theme.Guide : Ui.Theme.Border;
        glyph.Kind = text.EndsWith(".rpf", StringComparison.Ordinal) ? "archive" : "folder";
        title.Text = text;
        title.Foreground = active || hovered ? Ui.Theme.Text : Ui.Theme.TextDim;
        title.FontWeight = active ? FontWeight.Bold : FontWeight.Normal;
        AutomationProperties.SetName(close, $"Close {text}");
        InvalidateMeasure();
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

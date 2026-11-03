using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HikariHopper.Core;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The launcher of a tab with nothing open. Its DataContext is the tab's view model; <see cref="Tabs"/>
/// supplies the configured paths and the recent games. The games come from <see cref="GameCatalog"/>,
/// one group each, so a new game adds a group rather than a layout.
/// </summary>
public partial class StartPage : UserControl
{
    private ExplorerTabsViewModel? tabs;

    public StartPage()
    {
        InitializeComponent();
        VersionText.Text = AppVersion;
        RefreshGroups();
    }

    /// <summary>The app's version, as the project sets it ("0.0.2").</summary>
    public static string AppVersion { get; } =
        typeof(StartPage).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "";

    /// <summary>Raised by the launcher's Settings links; the menu row owns the Settings window.</summary>
    public event EventHandler? SettingsRequested;

    public ExplorerTabsViewModel? Tabs
    {
        get => tabs;
        set
        {
            if (tabs == value)
                return;
            if (tabs is not null)
            {
                tabs.PropertyChanged -= OnTabsPropertyChanged;
                tabs.GamePathSettings.PathsChanged -= OnPathsChanged;
            }
            tabs = value;
            if (tabs is not null)
            {
                tabs.PropertyChanged += OnTabsPropertyChanged;
                tabs.GamePathSettings.PathsChanged += OnPathsChanged;
            }
            RefreshGroups();
        }
    }

    /// <summary>What the launcher lists, group by group.</summary>
    internal IReadOnlyList<(SupportedGame Game, IReadOnlyList<LauncherEntry> Entries)> Entries { get; private set; } = [];

    /// <summary>The row drawn for each entry, in order.</summary>
    internal IReadOnlyList<LauncherRow> Rows => [.. Groups.Children.OfType<LauncherRow>()];

    private ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    private void OnTabsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerTabsViewModel.RecentGames))
            RefreshGroups();
    }

    private void OnPathsChanged(object? sender, EventArgs e) => RefreshGroups();

    private void RefreshGroups()
    {
        GamePathSettings? paths = tabs?.GamePathSettings;
        Entries = GameCatalog.Launcher(edition => paths?.PathFor(edition) ?? "", tabs?.RecentGames.Select(game => game.Path) ?? []);
        Groups.Children.Clear();
        foreach ((SupportedGame game, IReadOnlyList<LauncherEntry> entries) in Entries)
        {
            Groups.Children.Add(new TextBlock
            {
                Text = game.Title.ToUpperInvariant(),
                Margin = new Thickness(12, 10, 12, 4),
                Foreground = Ui.Theme.TextFaint,
                FontFamily = Ui.Theme.MonoFont,
                FontSize = 9,
                FontWeight = FontWeight.Bold,
                LetterSpacing = 1.2,
            });
            foreach (LauncherEntry entry in entries)
                Groups.Children.Add(new LauncherRow(entry, Activate));
        }
    }

    private void Activate(LauncherEntry entry)
    {
        if (entry.SetUp)
            Bridge?.OpenGameAsync(entry.Path);
        else
            SettingsRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnOpenStandalone(object? sender, RoutedEventArgs e) => _ = Bridge?.OpenArchiveDialog();

    private void OnSettings(object? sender, RoutedEventArgs e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// One launcher row: the game's mark tinted by edition, the edition (or folder) name and the path.
/// A game that isn't set up shows a dimmed mark and a link to Settings instead. Click, Enter or Space
/// activates it.
/// </summary>
public sealed class LauncherRow : Border
{
    private readonly LauncherEntry entry;
    private readonly Action<LauncherEntry> activate;

    public LauncherRow(LauncherEntry entry, Action<LauncherEntry> activate)
    {
        this.entry = entry;
        this.activate = activate;
        Height = 40;
        Padding = new Thickness(12, 0);
        CornerRadius = Ui.Theme.ItemCorner;
        Background = Brushes.Transparent;
        Focusable = true;
        Cursor = new Cursor(StandardCursorType.Hand);
        FocusAdorner = null;

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,110,*"), ColumnSpacing = 12 };
        Mark = GameMark(entry);
        grid.Children.Add(Mark);
        var title = new TextBlock
        {
            Text = entry.Title,
            Foreground = entry.SetUp ? Ui.Theme.Text : Ui.Theme.TextDim,
            FontFamily = Ui.Theme.UiFont,
            FontSize = 13,
            FontWeight = entry.SetUp ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        Detail = entry.SetUp
            ? new TextBlock
            {
                Text = entry.Path,
                Foreground = Ui.Theme.TextFaint,
                FontFamily = Ui.Theme.MonoFont,
                FontSize = Ui.Theme.SmallFontSize,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.LeadingCharacterEllipsis,
            }
            : new TextBlock
            {
                Inlines = [new Run("Add its installation in "), new Run("Settings…") { Foreground = Ui.Theme.Selection }],
                Foreground = Ui.Theme.TextFaint,
                FontFamily = Ui.Theme.UiFont,
                FontSize = Ui.Theme.FontSize,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
        Grid.SetColumn(Detail, 2);
        grid.Children.Add(Detail);
        Child = grid;

        PointerClick.Attach(this, _ => activate(entry));
        AutomationProperties.SetName(this, entry.SetUp ? $"Open {entry.Game.Title} {entry.Title}, {entry.Path}" : $"Set up {entry.Game.Title} in Settings");
    }

    public LauncherEntry Entry => entry;

    /// <summary>The game's mark, tinted by edition.</summary>
    public Control Mark { get; }

    /// <summary>The path, or the Settings link of a game that isn't set up.</summary>
    public TextBlock Detail { get; }

    /// <summary>
    /// The mark of <paramref name="entry"/>'s game. GTA V is the only game so far; another game
    /// brings its own mark here, tinted the same way by edition.
    /// </summary>
    private static Control GameMark(LauncherEntry entry) => new EditionMark
    {
        Width = 18,
        Height = 18,
        Enhanced = entry.Edition == GameSettings.EnhancedEdition,
        Opacity = entry.SetUp ? 1 : 0.45,
        VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>Lit while hovered, or focused from the keyboard (<c>:focus-visible</c>).</summary>
    public bool Lit => IsPointerOver || IsFocused && Classes.Contains(":focus-visible");

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty)
            RefreshBackground();
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        RefreshBackground();
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        RefreshBackground();
    }

    private void RefreshBackground() => Background = Lit ? Ui.Theme.HoverBg : Brushes.Transparent;

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key is Key.Enter or Key.Space)
        {
            activate(entry);
            e.Handled = true;
        }
    }
}

/// <summary>
/// Hosts the launcher: centred, <c>min(560, width - 64)</c> wide and a little above the middle.
/// </summary>
public sealed class StartPickerHost : Panel
{
    private static double PickerWidth(double width) => Math.Min(560, width - 64);

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsInfinity(availableSize.Width) ? 560 : PickerWidth(availableSize.Width);
        foreach (Control child in Children)
            child.Measure(new Size(Math.Max(0, width), double.PositiveInfinity));
        return default;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double width = Math.Max(0, PickerWidth(finalSize.Width));
        foreach (Control child in Children)
        {
            child.Measure(new Size(width, double.PositiveInfinity));
            double height = child.DesiredSize.Height;
            double x = QtCenter.Offset(finalSize.Width, width);
            double y = Math.Max(0, QtCenter.Offset(finalSize.Height, height) - 40);
            child.Arrange(new Rect(x, y, width, height));
        }
        return finalSize;
    }
}

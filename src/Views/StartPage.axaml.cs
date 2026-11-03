using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using HikariHopper.Core;
using HikariHopper.ViewModels;
using HikariHopper.Views.Controls;

namespace HikariHopper.Views;

/// <summary>
/// The start page of a tab without a workspace (port of <c>StartPage.qml</c>). Its DataContext is the
/// tab's view model; <see cref="Tabs"/> supplies the configured paths and the recent games.
/// </summary>
public partial class StartPage : UserControl
{
    // Settings owns installation discovery. This picker only chooses which of the two configured
    // editions to open.
    private static readonly (string Label, string Edition)[] Titles =
    [
        ("Grand Theft Auto V — Enhanced", GameSettings.EnhancedEdition),
        ("Grand Theft Auto V — Legacy", GameSettings.LegacyEdition),
    ];

    private ExplorerTabsViewModel? tabs;

    public StartPage()
    {
        InitializeComponent();
        TitleBox.ItemsSource = Titles.Select(title => new FlatComboOption(title.Label, title.Edition)).ToList();
        TitleBox.SelectedIndex = 0;
        TitleBox.SelectionChanged += (_, _) => RefreshPicker();
        RefreshPicker();
    }

    /// <summary>QML <c>tabs</c>.</summary>
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
                tabs.GamePathSettings.PropertyChanged -= OnGamePathsChanged;
            }
            tabs = value;
            if (tabs is not null)
            {
                tabs.PropertyChanged += OnTabsPropertyChanged;
                tabs.GamePathSettings.PropertyChanged += OnGamePathsChanged;
            }
            RefreshPicker();
            RefreshRecents();
        }
    }

    private ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    /// <summary><c>selectedEdition</c>.</summary>
    public string SelectedEdition => TitleBox.SelectedIndex < 0 ? "" : Titles[TitleBox.SelectedIndex].Edition;

    /// <summary><c>selectedPathValid</c>.</summary>
    public bool SelectedPathValid => SelectedEdition switch
    {
        GameSettings.EnhancedEdition => tabs?.GamePathSettings.EnhancedPathValid == true,
        GameSettings.LegacyEdition => tabs?.GamePathSettings.LegacyPathValid == true,
        _ => false,
    };

    private void OnTabsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerTabsViewModel.RecentGames))
            RefreshRecents();
    }

    private void OnGamePathsChanged(object? sender, PropertyChangedEventArgs e) => RefreshPicker();

    private void RefreshPicker()
    {
        bool valid = SelectedPathValid;
        OpenButton.IsEnabled = valid;
        PathHint.IsVisible = !valid;
    }

    private void RefreshRecents()
    {
        RecentRows.Children.Clear();
        IReadOnlyList<RecentGame> games = tabs?.RecentGames ?? [];
        Recents.IsVisible = games.Count > 0;
        foreach (RecentGame game in games)
            RecentRows.Children.Add(RecentRow(game));
    }

    private Control RecentRow(RecentGame game)
    {
        var row = new Border
        {
            Height = Ui.Theme.RowHeight,
            Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,104,*"), ColumnSpacing = 8 };
        grid.Children.Add(new FileGlyph { Kind = "folder", Margin = new Thickness(2, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        var edition = new TextBlock
        {
            Text = game.Edition,
            Foreground = Ui.Theme.TextRow,
            FontFamily = Ui.Theme.MonoFont,
            FontSize = Ui.Theme.SmallFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetColumn(edition, 1);
        grid.Children.Add(edition);
        var path = new TextBlock
        {
            Text = game.Path,
            Foreground = Ui.Theme.TextFaint,
            FontFamily = Ui.Theme.MonoFont,
            FontSize = Ui.Theme.SmallFontSize,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.LeadingCharacterEllipsis,
        };
        Grid.SetColumn(path, 2);
        grid.Children.Add(path);
        row.Child = grid;
        row.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsPointerOverProperty)
                row.Background = row.IsPointerOver ? Ui.Theme.HoverBg : Brushes.Transparent;
        };
        PointerClick.Attach(row, _ => tabs?.OpenRecentGame(game.Path));
        AutomationProperties.SetName(row, $"Open {game.Path}");
        return row;
    }

    private void OnOpen(object? sender, RoutedEventArgs e) => Bridge?.OpenConfiguredGame(SelectedEdition);

    private void OnOpenStandalone(object? sender, RoutedEventArgs e) => _ = Bridge?.OpenArchiveDialog();
}

/// <summary>
/// Hosts the start page picker: <c>anchors.centerIn: parent; anchors.verticalCenterOffset: -70;
/// width: Math.min(560, parent.width - 64)</c>.
/// </summary>
public sealed class StartPickerHost : Panel
{
    private double PickerWidth(double width) => Math.Min(560, width - 64);

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
            double y = QtCenter.Offset(finalSize.Height, height) - 70;
            child.Arrange(new Rect(x, y, width, height));
        }
        return finalSize;
    }
}

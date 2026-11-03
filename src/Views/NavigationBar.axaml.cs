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
using PathSegment = HikariHopper.ViewModels.PathSegment;

namespace HikariHopper.Views;

/// <summary>
/// Back/forward/up, the clickable address bar, the search field and the list/grid switch
/// (port of <c>NavigationBar.qml</c>). Its DataContext is the active tab's view model.
/// </summary>
public partial class NavigationBar : UserControl
{
    private ExplorerViewModel? bridge;

    public NavigationBar()
    {
        InitializeComponent();
        PointerClick.Attach(RootSegment, _ => OnRootClicked(), () => Bridge?.HasWorkspace == true);
        ViewModeSwitch.SideRequested += (_, side) => Bridge?.SetViewMode(side == 1 ? "grid" : "list");
        SearchField.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty)
            {
                Bridge?.SetSearch(SearchField.Text ?? "");
                RefreshSearchField();
            }
            else if (e.Property == IsKeyboardFocusWithinProperty)
            {
                RefreshSearchField();
            }
        };
        SearchField.KeyDown += OnSearchKeyDown;
        RefreshSearchField();

        WindowShortcuts.Register(this,
            new WindowShortcut("Ctrl+F", () => Bridge?.HasWorkspace == true, FocusSearch),
            new WindowShortcut("F3", () => Bridge?.HasWorkspace == true, FocusSearch),
            new WindowShortcut("Alt+Left", () => Bridge?.GoBack()),
            new WindowShortcut("Alt+Right", () => Bridge?.GoForward()),
            new WindowShortcut("Back", () => Bridge?.GoUp()));
    }

    private ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    /// <summary>QML <c>focusSearch()</c>: focus the search field and select its text.</summary>
    public void FocusSearch()
    {
        SearchField.Focus(NavigationMethod.Pointer);
        SearchField.SelectAll();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (bridge is not null)
        {
            bridge.PropertyChanged -= OnBridgePropertyChanged;
            bridge.SearchFocusRequested -= OnSearchFocusRequested;
        }
        bridge = Bridge;
        if (bridge is not null)
        {
            bridge.PropertyChanged += OnBridgePropertyChanged;
            bridge.SearchFocusRequested += OnSearchFocusRequested;
        }
        RefreshSegments();
        RefreshViewMode();
    }

    private void OnSearchFocusRequested(object? sender, EventArgs e) => FocusSearch();

    private void OnBridgePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ExplorerViewModel.NavigationPathSegments):
            case nameof(ExplorerViewModel.HasWorkspace):
            case nameof(ExplorerViewModel.HasGame):
            case nameof(ExplorerViewModel.InArchive):
            case nameof(ExplorerViewModel.GameName):
            case nameof(ExplorerViewModel.ArchiveRootName):
                RefreshSegments();
                break;
            case nameof(ExplorerViewModel.ViewMode):
                RefreshViewMode();
                break;
        }
    }

    private void RefreshSegments()
    {
        ExplorerViewModel? model = Bridge;
        bool hasWorkspace = model?.HasWorkspace == true;
        IReadOnlyList<PathSegment> parts = model?.NavigationPathSegments ?? [];
        // At the root the bar is empty: the tab already says where you are. Below it, the path starts with
        // a home mark that goes back to the root, and the location you are in reads brightest.
        RootSegment.IsVisible = !hasWorkspace || parts.Count > 0;
        if (hasWorkspace)
            RootSegment.SetHome(Ui.Theme.TextDim, model!.HasGame ? model.GameName : model.ArchiveRootName);
        else
            RootSegment.Set("Select the GTA V folder", false, Ui.Theme.TextDim);

        Segments.Children.Clear();
        for (int index = 0; index < parts.Count; index++)
        {
            PathSegment part = parts[index];
            bool last = index == parts.Count - 1;
            var segment = new NavigationSegment();
            segment.Set(part.Label, part.Archive,
                part.Archive ? Ui.Theme.Accent : last ? Ui.Theme.Text : Ui.Theme.TextDim);
            PointerClick.Attach(segment, _ => Bridge?.NavigateTree(part.Target));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
            row.Children.Add(new ChromeIcon
            {
                Kind = "chevron",
                Stroke = Ui.Theme.TextFaint,
                Thickness = 2,
                Width = 12,
                Height = 12,
                VerticalAlignment = VerticalAlignment.Center,
            });
            row.Children.Add(segment);
            Segments.Children.Add(row);
        }
    }

    private void RefreshViewMode()
    {
        ViewModeSwitch.SelectedIndex = Bridge?.ViewMode == "grid" ? 1 : 0;
    }

    private void RefreshSearchField()
    {
        bool hasText = !string.IsNullOrEmpty(SearchField.Text);
        SearchField.KeyCap = SearchField.IsKeyboardFocusWithin || hasText ? "Esc" : "F3";
        SearchField.LeadingColor = hasText ? Ui.Theme.Brass : Ui.Theme.TextFaint;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        // Keys.onEscapePressed: { clear(); focus = false }
        if (e.Key != Key.Escape)
            return;
        SearchField.Clear();
        TopLevel.GetTopLevel(this)?.FocusManager?.Focus(null);
        e.Handled = true;
    }

    private void OnRootClicked()
    {
        if (Bridge is not { } model)
            return;
        if (model.HasGame && model.InArchive)
            model.ShowGame();
        else if (model.InArchive)
            model.NavigateTree("archive://.");
        else
            model.Navigate(".");
    }

    private void OnBack(object? sender, RoutedEventArgs e) => Bridge?.GoBack();

    private void OnForward(object? sender, RoutedEventArgs e) => Bridge?.GoForward();

    private void OnUp(object? sender, RoutedEventArgs e) => Bridge?.GoUp();

    private void OnRefresh(object? sender, RoutedEventArgs e) => Bridge?.Refresh();


}

/// <summary>
/// One address-bar segment: 24px tall, its content plus 7px each side, a rounded hoverChrome
/// highlight while hovered, an optional 13px blue package mark and the mono label, centred.
/// </summary>
public sealed class NavigationSegment : Border
{
    private readonly ChromeIcon package = new()
    {
        Kind = "package",
        Stroke = Ui.Theme.Accent,
        Width = 13,
        Height = 13,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly ChromeIcon home = new()
    {
        Kind = "home",
        Width = 14,
        Height = 14,
        Thickness = 1.6,
        IsVisible = false,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock label = new()
    {
        FontFamily = Ui.Theme.MonoFont,
        FontSize = Ui.Theme.FontSize,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public NavigationSegment()
    {
        Height = 24;
        CornerRadius = Ui.Theme.InnerCorner;
        VerticalAlignment = VerticalAlignment.Center;
        Background = Brushes.Transparent;
        Padding = new Thickness(7, 0);
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(home);
        row.Children.Add(package);
        row.Children.Add(label);
        Child = row;
    }

    public string Label => label.Text ?? "";

    public void Set(string text, bool archive, IBrush ink)
    {
        label.Text = text;
        label.Foreground = ink;
        label.IsVisible = true;
        package.IsVisible = archive;
        home.IsVisible = false;
        Padding = new Thickness(7, 0);
        AutomationProperties.SetName(this, text);
    }

    /// <summary>Shows the home mark alone, which goes back to the root named <paramref name="root"/>.</summary>
    public void SetHome(IBrush ink, string root)
    {
        label.Text = "";
        label.IsVisible = false;
        package.IsVisible = false;
        home.IsVisible = true;
        home.Stroke = ink;
        Padding = new Thickness(6, 0);
        AutomationProperties.SetName(this, $"Back to {root}");
        ThemedToolTip.Set(this, root);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty)
            Background = IsPointerOver ? Ui.Theme.HoverChrome : Brushes.Transparent;
    }
}

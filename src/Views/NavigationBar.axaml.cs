using System.ComponentModel;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
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

        // Search mode: the query, where to look, and how the text is read.
        QueryField.PropertyChanged += (_, e) =>
        {
            if (e.Property == TextBox.TextProperty && Bridge is { IsSearching: true } model)
                model.SetSearchText(QueryField.Text ?? "");
        };
        QueryField.KeyDown += OnQueryKeyDown;
        PointerClick.Attach(ScopeButton, _ => OpenScopeMenu());
        // The toggles read as the text they change: the query's mono face, larger and bold.
        foreach (ChromeToolButton toggle in new[] { CaseToggle, WildcardToggle, RegexToggle })
            toggle.TemplateApplied += (_, e) =>
            {
                if (e.NameScope.Find<TextBlock>("PART_Text") is { } text)
                {
                    text.FontFamily = Ui.Theme.MonoFont;
                    text.FontSize = 13;
                    text.FontWeight = FontWeight.Bold;
                }
            };
        ThemedToolTip.Set(CaseToggle, "Match case");
        ThemedToolTip.Set(WildcardToggle, "Wildcards: * for any run of characters, ? for one; on by itself with either");
        ThemedToolTip.Set(RegexToggle, "Regular expression");

        // The address as text: a click on the bar's empty part edits it, a right-click offers copying and pasting.
        AddressBar.AddHandler(PointerPressedEvent, OnAddressBarPressed, RoutingStrategies.Bubble, handledEventsToo: true);
        AddressField.KeyDown += OnAddressKeyDown;
        AddressField.LostFocus += (_, _) => EndAddressEdit();

        WindowShortcuts.Register(this,
            new WindowShortcut("Ctrl+F", () => Bridge?.HasWorkspace == true, FocusSearch),
            new WindowShortcut("F3", () => Bridge?.HasWorkspace == true, FocusSearch),
            new WindowShortcut("Ctrl+Shift+F", () => Bridge?.HasWorkspace == true, () => BeginSearch(ViewModels.SearchScope.Everywhere)),
            new WindowShortcut("Ctrl+L", () => Bridge?.HasWorkspace == true, BeginAddressEdit),
            new WindowShortcut("Alt+D", () => Bridge?.HasWorkspace == true, BeginAddressEdit),
            new WindowShortcut("Alt+Left", () => Bridge?.GoBack()),
            new WindowShortcut("Alt+Right", () => Bridge?.GoForward()),
            new WindowShortcut("Back", () => Bridge?.GoUp()));
    }

    private ExplorerViewModel? Bridge => DataContext as ExplorerViewModel;

    /// <summary>The "…" mark the leading segments fold into when the path doesn't fit.</summary>
    internal NavigationSegment? OverflowSegment { get; private set; }

    /// <summary>The menu of folded segments, while it is open.</summary>
    internal RetroMenu? OverflowMenu { get; private set; }

    /// <summary>QML <c>focusSearch()</c>: focus the search field and select its text.</summary>
    public void FocusSearch()
    {
        // From a search, the folder's own search: the text comes along.
        if (Bridge is { IsSearching: true } model)
            model.BeginSearch(ViewModels.SearchScope.Folder);
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
        RefreshSearchRow();
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
                RefreshSearchRow();
                break;
            case nameof(ExplorerViewModel.ViewMode):
                RefreshViewMode();
                break;
            case nameof(ExplorerViewModel.IsSearching):
            case nameof(ExplorerViewModel.SearchScopeValue):
            case nameof(ExplorerViewModel.SearchText):
            case nameof(ExplorerViewModel.SearchError):
            case nameof(ExplorerViewModel.SearchMatchCase):
            case nameof(ExplorerViewModel.SearchWildcards):
            case nameof(ExplorerViewModel.SearchRegex):
                RefreshSearchRow();
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
            RootSegment.SetHome(Ui.Theme.TextDim, model!.ArchiveIsRoot || !model.HasGame ? model.ArchiveRootName : ExplorerViewModel.HomeTitle);
        else
            RootSegment.Set("Select the GTA V folder", false, Ui.Theme.TextDim);

        Segments.Children.Clear();
        // The overflow mark: the leading segments that don't fit, as a menu.
        var overflow = OverflowSegment = new NavigationSegment();
        overflow.Set("…", false, Ui.Theme.TextDim);
        AutomationProperties.SetName(overflow, "Earlier folders");
        PointerClick.Attach(overflow, _ => OpenOverflow(overflow));
        Segments.Children.Add(Row(overflow));
        for (int index = 0; index < parts.Count; index++)
        {
            PathSegment part = parts[index];
            bool last = index == parts.Count - 1;
            var segment = new NavigationSegment();
            segment.Set(part.Label, part.Archive,
                part.Archive ? Ui.Theme.Accent : last ? Ui.Theme.Text : Ui.Theme.TextDim);
            PointerClick.Attach(segment, _ => Bridge?.NavigateTree(part.Target));
            // The current folder is the one segment that can be cut short: its whole name is a hover away.
            if (last)
                ThemedToolTip.Set(segment, part.Label);
            Segments.Children.Add(Row(segment));
        }
    }

    /// <summary>A segment after its chevron. The row clips, so a segment folded away shows nothing.</summary>
    private static DockPanel Row(NavigationSegment segment)
    {
        var row = new DockPanel { ClipToBounds = true };
        row.Children.Add(new ChromeIcon
        {
            Kind = "chevron",
            Stroke = Ui.Theme.TextFaint,
            Thickness = 2,
            Width = 12,
            Height = 12,
            Margin = new Thickness(0, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            [DockPanel.DockProperty] = Dock.Left,
        });
        row.Children.Add(segment);
        return row;
    }

    /// <summary>The folded segments, outermost first, to go to one of them.</summary>
    private void OpenOverflow(Control anchor)
    {
        if (Bridge is not { } model || Segments.HiddenCount == 0)
            return;
        var menu = new RetroMenu();
        foreach (PathSegment part in model.NavigationPathSegments.Take(Segments.HiddenCount))
        {
            var item = new RetroMenuItem { Text = part.Label, IconName = part.Archive ? "package" : "folder" };
            item.Triggered += (_, _) => Bridge?.NavigateTree(part.Target);
            menu.Items.Add(item);
        }
        OverflowMenu = menu;
        menu.Closed += (_, _) => OverflowMenu = null;
        menu.OpenUnder(anchor, 4);
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
        // Back to the tab's root: an RPF opened on its own, or else the game.
        if (model.ArchiveIsRoot)
            model.NavigateTree("archive://.");
        else if (model.HasGame && model.InArchive)
            model.ShowGame();
        else
            model.Navigate(".");
    }

    /// <summary>Whether the address is shown as text being edited.</summary>
    internal bool EditingAddress => AddressField.IsVisible;

    /// <summary>The address bar's menu, while it is open.</summary>
    internal RetroMenu? AddressMenu { get; private set; }

    /// <summary>Shows the address as text, all of it selected, to copy, or to type or paste another over.</summary>
    public void BeginAddressEdit()
    {
        if (Bridge is not { HasWorkspace: true } model)
            return;
        AddressField.Text = model.AddressText;
        AddressField.Invalid = false;
        AddressBar.IsVisible = false;
        AddressField.IsVisible = true;
        AddressField.Focus(NavigationMethod.Pointer);
        AddressField.SelectAll();
    }

    private void EndAddressEdit()
    {
        if (!AddressField.IsVisible)
            return;
        AddressField.IsVisible = false;
        AddressBar.IsVisible = true;
    }

    private void OnAddressKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            EndAddressEdit();
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Return)
        {
            e.Handled = true;
            // A path that can't be gone to stays, marked, for fixing; the status bar says why.
            if (Bridge?.GoToAddress(AddressField.Text ?? "") == true)
                EndAddressEdit();
            else
                AddressField.Invalid = true;
        }
        else
        {
            AddressField.Invalid = false;
        }
    }

    private void OnAddressBarPressed(object? sender, PointerPressedEventArgs e)
    {
        // Segments and the refresh button keep their own clicks; the rest of the bar is for the address.
        if (e.Source is Visual source && source.GetSelfAndVisualAncestors()
                .Any(visual => visual is NavigationSegment or ChromeToolButton))
            return;
        PointerPointProperties point = e.GetCurrentPoint(AddressBar).Properties;
        if (point.IsLeftButtonPressed)
        {
            e.Handled = true;
            Avalonia.Threading.Dispatcher.UIThread.Post(BeginAddressEdit);
        }
        else if (point.IsRightButtonPressed)
        {
            e.Handled = true;
            OpenAddressMenu(e.GetPosition(AddressBar));
        }
    }

    /// <summary>Copy address, Edit address, and Paste and go.</summary>
    private void OpenAddressMenu(Point at)
    {
        if (Bridge is not { HasWorkspace: true })
            return;
        var menu = new RetroMenu();
        var copy = new RetroMenuItem { Text = "Copy address", IconName = "copy" };
        copy.Triggered += (_, _) => Bridge?.CopyAddress();
        var change = new RetroMenuItem { Text = "Edit address", IconName = "pencil", ShortcutText = "Ctrl+L" };
        change.Triggered += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(BeginAddressEdit);
        var paste = new RetroMenuItem { Text = "Paste and go", IconName = "clipboard" };
        paste.Triggered += async (_, _) =>
        {
            if (await Infrastructure.AppServices.Clipboard.GetTextAsync() is { } text && text.Trim().Length > 0)
                Bridge?.GoToAddress(text.Split('\n')[0]);
        };
        menu.Items.Add(copy);
        menu.Items.Add(change);
        menu.Items.Add(paste);
        AddressMenu = menu;
        menu.Closed += (_, _) => AddressMenu = null;
        menu.Open(AddressBar, at.X, at.Y);
    }

    /// <summary>Raised when Enter or Down in the query should move to the results.</summary>
    public event EventHandler? ResultsFocusRequested;

    /// <summary>Starts searching <paramref name="scope"/>, the query focused with its text selected.</summary>
    public void BeginSearch(ViewModels.SearchScope scope)
    {
        if (Bridge is not { HasWorkspace: true } model)
            return;
        EndAddressEdit();
        model.BeginSearch(scope);
        RefreshSearchRow();
        QueryField.Focus(NavigationMethod.Pointer);
        QueryField.SelectAll();
    }

    /// <summary>Each scope's icon, one word, and what it covers, which its menu item says as a tooltip.</summary>
    private static readonly (ViewModels.SearchScope Scope, string Icon, string Name, string Covers)[] Scopes =
    [
        (ViewModels.SearchScope.Folder, "folder", "Folder", "This folder only · Ctrl+F"),
        (ViewModels.SearchScope.FolderAndBelow, "list-tree", "Subfolders", "This folder and everything in it"),
        (ViewModels.SearchScope.Archive, "package", "RPF", "The RPF you're in, nested ones included"),
        (ViewModels.SearchScope.Everywhere, "house", "Game", "The whole game · Ctrl+Shift+F"),
    ];

    private ViewModels.SearchScope ShownScope => Bridge is { IsSearching: true } model ? model.SearchScopeValue : ViewModels.SearchScope.Folder;

    /// <summary>The search row in the address bar's place while searching, showing what the tab searches.</summary>
    private void RefreshSearchRow()
    {
        bool searching = Bridge?.IsSearching == true;
        // However the search started (Ctrl+Shift+F, the Go menu), its query takes the keyboard.
        if (searching && !SearchRow.IsVisible)
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (Bridge?.IsSearching != true || QueryField.IsKeyboardFocusWithin)
                    return;
                QueryField.Focus(NavigationMethod.Pointer);
                QueryField.SelectAll();
            });
        SearchRow.IsVisible = searching;
        if (searching)
        {
            AddressField.IsVisible = false;
            AddressBar.IsVisible = false;
        }
        else if (!AddressField.IsVisible)
        {
            AddressBar.IsVisible = true;
        }
        SearchField.IsVisible = !searching;
        // The selector goes where the query is: before the folder's box, or at the start of the search row.
        Panel slot = searching ? SearchScopeSlot : ScopeSlot;
        if (ScopeWell.Parent != slot)
        {
            (ScopeWell.Parent as Panel)?.Children.Remove(ScopeWell);
            slot.Children.Add(ScopeWell);
        }
        var (_, icon, name, covers) = Scopes.Single(item => item.Scope == ShownScope);
        ScopeIcon.IconName = icon;
        ScopeText.Text = name;
        ThemedToolTip.Set(ScopeButton, covers);
        ScopeWell.IsVisible = Bridge?.HasWorkspace == true;
        if (Bridge is not { } model || !searching)
            return;
        if (QueryField.Text != model.SearchText)
            QueryField.Text = model.SearchText;
        QueryField.Invalid = model.SearchError.Length > 0;
        QueryError.Text = model.SearchError;
        CaseToggle.Raised = model.SearchMatchCase;
        WildcardToggle.Raised = model.SearchWildcards;
        RegexToggle.Raised = model.SearchRegex;
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Bridge?.EndSearch();
            e.Handled = true;
        }
        else if (e.Key is Key.Enter or Key.Return or Key.Down && Bridge is { EntriesModel.Count: > 0 } model)
        {
            if (model.SelectedIndex < 0)
                model.SelectEntry(0);
            ResultsFocusRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    /// <summary>The scopes: the folder (its own search), the folder and below, the RPF shown, the whole game.</summary>
    private void OpenScopeMenu()
    {
        if (Bridge is not { HasWorkspace: true } model)
            return;
        // The names only; what each covers is its tooltip.
        var menu = new RetroMenu { Width = 150 + 2 * Ui.Theme.MenuPadding };
        ViewModels.SearchScope shown = ShownScope;
        foreach (var (scope, icon, name, covers) in Scopes)
        {
            var item = new RetroMenuItem
            {
                Text = name, IconName = icon, MinWidth = 0,
                IsEnabled = scope != ViewModels.SearchScope.Archive || model.CanSearchArchive,
            };
            _ = shown;
            ThemedToolTip.Set(item, covers);
            item.Triggered += (_, _) =>
            {
                if (scope == ViewModels.SearchScope.Folder)
                {
                    if (model.IsSearching)
                        model.BeginSearch(scope);
                    SearchField.Focus(NavigationMethod.Pointer);
                }
                else
                {
                    BeginSearch(scope);
                }
            };
            menu.Items.Add(item);
        }
        ScopeMenu = menu;
        menu.Closed += (_, _) => ScopeMenu = null;
        menu.OpenUnder(ScopeButton, 4);
    }

    /// <summary>The scope menu, while it is open.</summary>
    internal RetroMenu? ScopeMenu { get; private set; }

    private void OnCaseToggle(object? sender, RoutedEventArgs e) => Bridge?.ToggleSearchMatchCase();

    private void OnWildcardToggle(object? sender, RoutedEventArgs e) => Bridge?.ToggleSearchWildcards();

    private void OnRegexToggle(object? sender, RoutedEventArgs e) => Bridge?.ToggleSearchRegex();

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
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public NavigationSegment()
    {
        Height = 24;
        CornerRadius = Ui.Theme.InnerCorner;
        VerticalAlignment = VerticalAlignment.Center;
        Background = Brushes.Transparent;
        Padding = new Thickness(7, 0);
        // A dock, not a stack, so a narrower width reaches the label and trims it.
        var row = new DockPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(home, Dock.Left);
        DockPanel.SetDock(package, Dock.Left);
        package.Margin = new Thickness(0, 0, 6, 0);
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

    /// <summary>Shows the home mark alone, which goes back to the root called <paramref name="root"/>.</summary>
    public void SetHome(IBrush ink, string root)
    {
        label.Text = "";
        label.IsVisible = false;
        package.IsVisible = false;
        home.IsVisible = true;
        home.Stroke = ink;
        Padding = new Thickness(6, 0);
        AutomationProperties.SetName(this, root);
        ThemedToolTip.Set(this, root);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsPointerOverProperty)
            Background = IsPointerOver ? Ui.Theme.HoverChrome : Brushes.Transparent;
    }
}
